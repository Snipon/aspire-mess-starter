data "azurerm_client_config" "current" {}

resource "random_string" "suffix" {
  length  = 6
  upper   = false
  special = false
}

locals {
  # Globally unique names: <name><suffix> (alphanumeric only, fits ACR/Storage/Key Vault limits).
  unique = "${var.name}${random_string.suffix.result}"

  # Container App FQDNs follow <app>.<environment default domain> (internal ingress adds ".internal").
  # They are derived here rather than read from the apps to avoid a cms <-> api dependency cycle
  # (cms needs the api webhook URL, api needs the cms URL).
  cms_url         = "https://cms.${azurerm_container_app_environment.this.default_domain}"
  api_url         = "https://api.${azurerm_container_app_environment.this.default_domain}"
  web_url         = "https://web.${azurerm_container_app_environment.this.default_domain}"
  meilisearch_url = "https://meilisearch.internal.${azurerm_container_app_environment.this.default_domain}"
  # Seq's UI is public only through seq-proxy (staff sign-in); the apps ingest logs over Seq's internal ingress.
  seq_url        = "https://seq-proxy.${azurerm_container_app_environment.this.default_domain}"
  seq_ingest_url = "https://seq.internal.${azurerm_container_app_environment.this.default_domain}"
}

resource "azurerm_resource_group" "this" {
  name     = "rg-${var.name}"
  location = var.location
  tags     = var.tags
}

resource "azurerm_log_analytics_workspace" "this" {
  name                = "log-${local.unique}"
  location            = azurerm_resource_group.this.location
  resource_group_name = azurerm_resource_group.this.name
  sku                 = "PerGB2018"
  retention_in_days   = var.log_retention_days
  tags                = var.tags
}

resource "azurerm_container_registry" "this" {
  name                = "acr${local.unique}"
  location            = azurerm_resource_group.this.location
  resource_group_name = azurerm_resource_group.this.name
  sku                 = "Basic"
  admin_enabled       = false
  tags                = var.tags
}

# Identity used by all container apps to pull images and read Key Vault secrets.
resource "azurerm_user_assigned_identity" "apps" {
  name                = "id-${var.name}-apps"
  location            = azurerm_resource_group.this.location
  resource_group_name = azurerm_resource_group.this.name
  tags                = var.tags
}

resource "azurerm_role_assignment" "apps_acr_pull" {
  scope                = azurerm_container_registry.this.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.apps.principal_id
  principal_type       = "ServicePrincipal"
}
