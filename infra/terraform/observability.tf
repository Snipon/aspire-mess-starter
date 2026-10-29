# --- Aspire dashboard (Container Apps .NET component) -----------------------------------------------
#
# Shows the OpenTelemetry logs, traces and metrics of every app in the environment. azurerm has no
# resource for managedEnvironments/dotNetComponents, hence azapi. 2025-10-02-preview is the version
# Aspire 13.6 itself deploys and the newest one azapi 2.13 validates (stable 2026-07-01 is not in its
# schema yet).

resource "azapi_resource" "aspire_dashboard" {
  type      = "Microsoft.App/managedEnvironments/dotNetComponents@2025-10-02-preview"
  name      = "aspire-dashboard"
  parent_id = azurerm_container_app_environment.this.id

  body = {
    properties = {
      componentType = "AspireDashboard"
    }
  }
}

# Opening the dashboard requires write access (Contributor/Owner) on the environment, assigned to
# the user directly: group-based assignments are not honoured by the dashboard.
resource "azurerm_role_assignment" "aspire_dashboard_users" {
  for_each = toset(var.aspire_dashboard_user_object_ids)

  scope                = azurerm_container_app_environment.this.id
  role_definition_name = "Contributor"
  principal_id         = each.value
  principal_type       = "User"
}

# --- Azure Monitor: notification target ----------------------------------------------------------

resource "azurerm_monitor_action_group" "ops" {
  name                = "ag-${var.name}-ops"
  resource_group_name = azurerm_resource_group.this.name
  short_name          = substr("${var.name}ops", 0, 12)
  tags                = var.tags

  dynamic "email_receiver" {
    for_each = var.alert_email_addresses
    content {
      name                    = "email-${email_receiver.key}"
      email_address           = email_receiver.value
      use_common_alert_schema = true
    }
  }

  dynamic "webhook_receiver" {
    for_each = var.alert_webhook_url == null ? [] : [var.alert_webhook_url]
    content {
      name                    = "webhook"
      service_uri             = webhook_receiver.value
      use_common_alert_schema = true
    }
  }
}

# --- Availability tests (Application Insights standard tests) --------------------------------------
#
# Application Insights is only used for the synthetic tests; app telemetry goes to the Aspire
# dashboard and Seq. /health returns just "Healthy"/"Unhealthy" (ServiceDefaults), so it is safe to
# probe from the internet.

resource "azurerm_application_insights" "this" {
  name                = "appi-${var.name}"
  location            = azurerm_resource_group.this.location
  resource_group_name = azurerm_resource_group.this.name
  workspace_id        = azurerm_log_analytics_workspace.this.id
  application_type    = "web"
  tags                = var.tags
}

locals {
  availability_tests = {
    web = "${local.web_url}/"
    api = "${local.api_url}/health"
    cms = "${local.cms_url}/health"
  }
}

resource "azurerm_application_insights_standard_web_test" "this" {
  for_each = local.availability_tests

  name                    = "avail-${each.key}"
  location                = azurerm_resource_group.this.location
  resource_group_name     = azurerm_resource_group.this.name
  application_insights_id = azurerm_application_insights.this.id
  geo_locations           = var.availability_test_locations
  frequency               = 300
  timeout                 = 30
  retry_enabled           = true
  enabled                 = true
  tags                    = var.tags

  request {
    url = each.value
  }

  validation_rules {
    expected_status_code = 200
    ssl_check_enabled    = true
  }
}

resource "azurerm_monitor_metric_alert" "availability" {
  for_each = azurerm_application_insights_standard_web_test.this

  name                = "alert-${var.name}-availability-${each.key}"
  resource_group_name = azurerm_resource_group.this.name
  scopes              = [each.value.id, azurerm_application_insights.this.id]
  description         = "${local.availability_tests[each.key]} is failing from most test locations."
  severity            = 1
  frequency           = "PT1M"
  window_size         = "PT5M"
  tags                = var.tags

  application_insights_web_test_location_availability_criteria {
    web_test_id           = each.value.id
    component_id          = azurerm_application_insights.this.id
    failed_location_count = max(1, length(var.availability_test_locations) - 2)
  }

  action {
    action_group_id = azurerm_monitor_action_group.ops.id
  }
}

# --- Container Apps metric alerts ------------------------------------------------------------------

locals {
  container_apps = {
    cms         = azurerm_container_app.cms
    api         = azurerm_container_app.api
    web         = azurerm_container_app.web
    meilisearch = azurerm_container_app.meilisearch
    seq         = azurerm_container_app.seq
    seq-proxy   = azurerm_container_app.seq_proxy
  }

  # Apps with external ingress (5xx alert) and apps that autoscale (replicas-at-max alert).
  external_apps   = { for k in ["cms", "api", "web"] : k => local.container_apps[k] }
  autoscaled_apps = { for k, app in local.container_apps : k => app if app.template[0].max_replicas > 1 }

  container_app_usage_alerts = {
    cpu    = { metric = "CpuPercentage", label = "CPU" }
    memory = { metric = "MemoryPercentage", label = "Memory" }
  }
}

resource "azurerm_monitor_metric_alert" "app_5xx" {
  for_each = local.external_apps

  name                = "alert-${var.name}-${each.key}-5xx"
  resource_group_name = azurerm_resource_group.this.name
  scopes              = [each.value.id]
  description         = "${each.key} returned more than ${var.http_5xx_alert_threshold} 5xx responses in 5 minutes."
  severity            = 2
  frequency           = "PT1M"
  window_size         = "PT5M"
  tags                = var.tags

  criteria {
    metric_namespace = "Microsoft.App/containerApps"
    metric_name      = "Requests"
    aggregation      = "Total"
    operator         = "GreaterThan"
    threshold        = var.http_5xx_alert_threshold

    dimension {
      name     = "statusCodeCategory"
      operator = "Include"
      values   = ["5xx"]
    }
  }

  action {
    action_group_id = azurerm_monitor_action_group.ops.id
  }
}

# RestartCount is cumulative per replica: the alert fires once a replica has restarted (crash loop,
# failed liveness probe, OOM) and resolves when that replica is replaced.
resource "azurerm_monitor_metric_alert" "app_restarts" {
  for_each = local.container_apps

  name                = "alert-${var.name}-${each.key}-restarts"
  resource_group_name = azurerm_resource_group.this.name
  scopes              = [each.value.id]
  description         = "A ${each.key} replica restarted more than twice."
  severity            = 2
  frequency           = "PT5M"
  window_size         = "PT15M"
  tags                = var.tags

  criteria {
    metric_namespace = "Microsoft.App/containerApps"
    metric_name      = "RestartCount"
    aggregation      = "Maximum"
    operator         = "GreaterThan"
    threshold        = 2
  }

  action {
    action_group_id = azurerm_monitor_action_group.ops.id
  }
}

resource "azurerm_monitor_metric_alert" "app_usage" {
  for_each = {
    for pair in setproduct(keys(local.container_apps), keys(local.container_app_usage_alerts)) :
    "${pair[0]}-${pair[1]}" => { app = pair[0], usage = local.container_app_usage_alerts[pair[1]] }
  }

  name                = "alert-${var.name}-${each.key}"
  resource_group_name = azurerm_resource_group.this.name
  scopes              = [local.container_apps[each.value.app].id]
  description         = "${each.value.app}: ${each.value.usage.label} above 85% of its limit for 15 minutes."
  severity            = 2
  frequency           = "PT5M"
  window_size         = "PT15M"
  tags                = var.tags

  criteria {
    metric_namespace = "Microsoft.App/containerApps"
    metric_name      = each.value.usage.metric
    aggregation      = "Average"
    operator         = "GreaterThan"
    threshold        = 85
  }

  action {
    action_group_id = azurerm_monitor_action_group.ops.id
  }
}

resource "azurerm_monitor_metric_alert" "app_replicas_at_max" {
  for_each = local.autoscaled_apps

  name                = "alert-${var.name}-${each.key}-replicas-max"
  resource_group_name = azurerm_resource_group.this.name
  scopes              = [each.value.id]
  description         = "${each.key} has run at its maximum of ${each.value.template[0].max_replicas} replicas for 15 minutes."
  severity            = 3
  frequency           = "PT5M"
  window_size         = "PT15M"
  tags                = var.tags

  criteria {
    metric_namespace = "Microsoft.App/containerApps"
    metric_name      = "Replicas"
    aggregation      = "Minimum"
    operator         = "GreaterThanOrEqual"
    threshold        = each.value.template[0].max_replicas
  }

  action {
    action_group_id = azurerm_monitor_action_group.ops.id
  }
}

# --- Azure SQL metric alerts -----------------------------------------------------------------------

locals {
  sql_alerts = {
    cpu = {
      metric      = "cpu_percent", aggregation = "Average", threshold = 80, severity = 2
      description = "Database CPU above 80% for 15 minutes."
    }
    storage = {
      metric      = "storage_percent", aggregation = "Maximum", threshold = 85, severity = 2
      description = "Database storage above 85% of the maximum size."
    }
    connection-failed = {
      metric      = "connection_failed", aggregation = "Total", threshold = 5, severity = 1
      description = "More than 5 failed database connections in 15 minutes (credentials, firewall, outage)."
    }
  }
}

resource "azurerm_monitor_metric_alert" "sql" {
  for_each = local.sql_alerts

  name                = "alert-${var.name}-sql-${each.key}"
  resource_group_name = azurerm_resource_group.this.name
  scopes              = [azurerm_mssql_database.umbraco.id]
  description         = each.value.description
  severity            = each.value.severity
  frequency           = "PT5M"
  window_size         = "PT15M"
  tags                = var.tags

  criteria {
    metric_namespace = "Microsoft.Sql/servers/databases"
    metric_name      = each.value.metric
    aggregation      = each.value.aggregation
    operator         = "GreaterThan"
    threshold        = each.value.threshold
  }

  action {
    action_group_id = azurerm_monitor_action_group.ops.id
  }
}
