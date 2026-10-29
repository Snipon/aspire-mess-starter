resource "azurerm_container_app_environment" "this" {
  name                       = "cae-${var.name}"
  location                   = azurerm_resource_group.this.location
  resource_group_name        = azurerm_resource_group.this.name
  log_analytics_workspace_id = azurerm_log_analytics_workspace.this.id
  tags                       = var.tags
}

locals {
  kv_secret_ids = { for k, s in azurerm_key_vault_secret.this : k => s.versionless_id }
  acr_images    = "${azurerm_container_registry.this.login_server}/aspire-test"
}

# --- meilisearch (internal only) ---------------------------------------------
#
# The search index is a cache that the api can always rebuild from the CMS, so Meilisearch runs on
# ephemeral replica storage (EmptyDir) instead of a network share: LMDB must not live on SMB, and
# Container Apps briefly runs the old and the new revision side by side on every update, which would
# make two processes open the same data directory. Each replica/revision now has its own private
# directory; the api detects an empty index within ~1 minute and rebuilds it (SearchIndexInitializer).

resource "azurerm_container_app" "meilisearch" {
  name                         = "meilisearch"
  container_app_environment_id = azurerm_container_app_environment.this.id
  resource_group_name          = azurerm_resource_group.this.name
  revision_mode                = "Single"
  tags                         = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.apps.id]
  }

  secret {
    name                = "meilisearch-master-key"
    identity            = azurerm_user_assigned_identity.apps.id
    key_vault_secret_id = local.kv_secret_ids["meilisearch-master-key"]
  }

  ingress {
    external_enabled = false
    target_port      = 7700
    transport        = "http"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    # One replica: every replica would hold its own (independently built) index. During a revision
    # update the old and new revision overlap briefly; that is safe because storage is not shared.
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "meilisearch"
      image  = var.meilisearch_image
      cpu    = 0.5
      memory = "1Gi"

      env {
        name  = "MEILI_ENV"
        value = "production"
      }
      env {
        name  = "MEILI_DB_PATH"
        value = "/meili_data/data.ms"
      }
      env {
        name  = "MEILI_NO_ANALYTICS"
        value = "true"
      }
      env {
        name        = "MEILI_MASTER_KEY"
        secret_name = "meilisearch-master-key"
      }

      volume_mounts {
        name = "data"
        path = "/meili_data"
      }
    }

    volume {
      name         = "data"
      storage_type = "EmptyDir"
    }
  }

  depends_on = [time_sleep.rbac_propagation]
}

# --- cms (Umbraco, external: backoffice + media + Delivery API) ---------------

resource "azurerm_container_app" "cms" {
  name                         = "cms"
  container_app_environment_id = azurerm_container_app_environment.this.id
  resource_group_name          = azurerm_resource_group.this.name
  revision_mode                = "Single"
  tags                         = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.apps.id]
  }

  registry {
    server   = azurerm_container_registry.this.login_server
    identity = azurerm_user_assigned_identity.apps.id
  }

  dynamic "secret" {
    for_each = toset([
      "umbraco-db-connection-string",
      "umbraco-admin-password",
      "delivery-api-key",
      "hmac-secret",
      "webhook-secret",
      "media-storage-connection-string",
      "seq-ingestion-api-key",
    ])
    content {
      name                = secret.value
      identity            = azurerm_user_assigned_identity.apps.id
      key_vault_secret_id = local.kv_secret_ids[secret.value]
    }
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    # Umbraco keeps per-instance state (data protection keys, caches): run exactly one replica.
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "cms"
      image  = "${local.acr_images}/cms:${var.image_tag}"
      cpu    = 1
      memory = "2Gi"

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }
      env {
        name  = "ASPNETCORE_HTTP_PORTS"
        value = "8080"
      }
      env {
        # TLS terminates at the Container Apps ingress; honour X-Forwarded-Proto/Host.
        name  = "ASPNETCORE_FORWARDEDHEADERS_ENABLED"
        value = "true"
      }
      env {
        name        = "ConnectionStrings__umbracoDbDSN"
        secret_name = "umbraco-db-connection-string"
      }
      env {
        name  = "ConnectionStrings__umbracoDbDSN_ProviderName"
        value = "Microsoft.Data.SqlClient"
      }
      env {
        name  = "Umbraco__CMS__WebRouting__UmbracoApplicationUrl"
        value = local.cms_url
      }
      # Unattended install creates the schema + admin user on first start; no-op once installed.
      env {
        name  = "Umbraco__CMS__Unattended__InstallUnattended"
        value = "true"
      }
      env {
        name  = "Umbraco__CMS__Unattended__UpgradeUnattended"
        value = "true"
      }
      env {
        name  = "Umbraco__CMS__Unattended__UnattendedUserName"
        value = "Admin"
      }
      env {
        name  = "Umbraco__CMS__Unattended__UnattendedUserEmail"
        value = var.umbraco_admin_email
      }
      env {
        name        = "Umbraco__CMS__Unattended__UnattendedUserPassword"
        secret_name = "umbraco-admin-password"
      }
      env {
        name  = "Umbraco__CMS__DeliveryApi__Enabled"
        value = "true"
      }
      env {
        # Demo articles, blog posts and images (src/Cms/Headless/ContentSeeder.cs); remove for a real project.
        name  = "DemoContent__Enabled"
        value = "true"
      }
      env {
        name  = "Umbraco__CMS__DeliveryApi__PublicAccess"
        value = "false"
      }
      env {
        name        = "Umbraco__CMS__DeliveryApi__ApiKey"
        secret_name = "delivery-api-key"
      }
      env {
        name        = "Umbraco__CMS__Imaging__HMACSecretKey"
        secret_name = "hmac-secret"
      }
      env {
        name        = "Umbraco__Storage__AzureBlob__Media__ConnectionString"
        secret_name = "media-storage-connection-string"
      }
      env {
        name  = "Umbraco__Storage__AzureBlob__Media__ContainerName"
        value = azurerm_storage_container.media.name
      }
      env {
        name  = "ContentWebhook__TargetUrl"
        value = "${local.api_url}/api/webhooks/content"
      }
      env {
        name        = "ContentWebhook__Secret"
        secret_name = "webhook-secret"
      }
      # Logs/traces to Seq (ServiceDefaults AddSeqEndpoint), same keys Aspire injects locally.
      env {
        name  = "ConnectionStrings__seq"
        value = local.seq_url
      }
      env {
        name        = "Aspire__Seq__ApiKey"
        secret_name = "seq-ingestion-api-key"
      }

      # Umbraco boots (unattended install / migrations) before Kestrel listens. Allow up to
      # 60 x 10 s = 10 minutes instead of the default TCP startup budget, so a first install or an
      # upgrade against a small Azure SQL tier is not killed half-way.
      startup_probe {
        transport               = "TCP"
        port                    = 8080
        initial_delay           = 10
        interval_seconds        = 10
        timeout                 = 5
        failure_count_threshold = 60
      }
    }
  }

  depends_on = [time_sleep.rbac_propagation]
}

# --- api (typed content/search API, external) --------------------------------

resource "azurerm_container_app" "api" {
  name                         = "api"
  container_app_environment_id = azurerm_container_app_environment.this.id
  resource_group_name          = azurerm_resource_group.this.name
  revision_mode                = "Single"
  tags                         = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.apps.id]
  }

  registry {
    server   = azurerm_container_registry.this.login_server
    identity = azurerm_user_assigned_identity.apps.id
  }

  dynamic "secret" {
    for_each = toset([
      "meilisearch-connection-string",
      "delivery-api-key",
      "hmac-secret",
      "webhook-secret",
      "reindex-secret",
      "seq-ingestion-api-key",
    ])
    content {
      name                = secret.value
      identity            = azurerm_user_assigned_identity.apps.id
      key_vault_secret_id = local.kv_secret_ids[secret.value]
    }
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas = 1
    max_replicas = 3

    container {
      name   = "api"
      image  = "${local.acr_images}/api:${var.image_tag}"
      cpu    = 0.5
      memory = "1Gi"

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }
      env {
        name  = "ASPNETCORE_HTTP_PORTS"
        value = "8080"
      }
      env {
        name  = "ASPNETCORE_FORWARDEDHEADERS_ENABLED"
        value = "true"
      }
      env {
        name        = "ConnectionStrings__meilisearch"
        secret_name = "meilisearch-connection-string"
      }
      env {
        name  = "Providers__Content"
        value = "Umbraco"
      }
      env {
        name  = "Providers__Search"
        value = "Meilisearch"
      }
      env {
        # Drupal Commerce is not deployed (local only), so the shop endpoints are off.
        name  = "Providers__Commerce"
        value = "None"
      }
      # Same logical address as in Aspire; resolved by Microsoft.Extensions.ServiceDiscovery
      # from the services__cms__* entry below (what Aspire's WithReference(cms) injects locally).
      env {
        name  = "Umbraco__BaseUrl"
        value = "https+http://cms"
      }
      env {
        name  = "services__cms__https__0"
        value = local.cms_url
      }
      env {
        name  = "Umbraco__PublicBaseUrl"
        value = local.cms_url
      }
      env {
        name        = "Umbraco__DeliveryApiKey"
        secret_name = "delivery-api-key"
      }
      env {
        name        = "Umbraco__HmacSecretKey"
        secret_name = "hmac-secret"
      }
      env {
        name        = "Webhooks__Secret"
        secret_name = "webhook-secret"
      }
      env {
        name        = "Search__ReindexSecret"
        secret_name = "reindex-secret"
      }
      # Logs/traces to Seq (ServiceDefaults AddSeqEndpoint), same keys Aspire injects locally.
      env {
        name  = "ConnectionStrings__seq"
        value = local.seq_url
      }
      env {
        name        = "Aspire__Seq__ApiKey"
        secret_name = "seq-ingestion-api-key"
      }

      # /alive only runs the "live"-tagged self check, so a Meilisearch or CMS outage never restarts the api.
      liveness_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/alive"
      }
    }
  }

  depends_on = [time_sleep.rbac_propagation, azurerm_container_app.meilisearch]
}

# --- web (Astro SSR, external) -----------------------------------------------

resource "azurerm_container_app" "web" {
  name                         = "web"
  container_app_environment_id = azurerm_container_app_environment.this.id
  resource_group_name          = azurerm_resource_group.this.name
  revision_mode                = "Single"
  tags                         = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.apps.id]
  }

  registry {
    server   = azurerm_container_registry.this.login_server
    identity = azurerm_user_assigned_identity.apps.id
  }

  secret {
    name                = "seq-ingestion-api-key"
    identity            = azurerm_user_assigned_identity.apps.id
    key_vault_secret_id = local.kv_secret_ids["seq-ingestion-api-key"]
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas = 1
    max_replicas = 3

    container {
      name   = "web"
      image  = "${local.acr_images}/web:${var.image_tag}"
      cpu    = 0.5
      memory = "1Gi"

      env {
        name  = "HOST"
        value = "0.0.0.0"
      }
      env {
        name  = "PORT"
        value = "8080"
      }
      env {
        name  = "API_URL"
        value = local.api_url
      }
      # Logs/traces to Seq (otel.mjs), same keys Aspire injects locally.
      env {
        name  = "ConnectionStrings__seq"
        value = local.seq_url
      }
      env {
        name        = "Aspire__Seq__ApiKey"
        secret_name = "seq-ingestion-api-key"
      }
    }
  }

  depends_on = [time_sleep.rbac_propagation, azurerm_container_app.api]
}
