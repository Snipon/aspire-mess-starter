resource "azurerm_key_vault" "this" {
  name                       = "kv-${local.unique}"
  location                   = azurerm_resource_group.this.location
  resource_group_name        = azurerm_resource_group.this.name
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  sku_name                   = "standard"
  rbac_authorization_enabled = true
  soft_delete_retention_days = 7
  purge_protection_enabled   = false
  tags                       = var.tags
}

# The identity running Terraform writes the secrets.
resource "azurerm_role_assignment" "deployer_kv_secrets_officer" {
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = data.azurerm_client_config.current.object_id
}

# The container apps read them.
resource "azurerm_role_assignment" "apps_kv_secrets_user" {
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.apps.principal_id
  principal_type       = "ServicePrincipal"
}

# RBAC assignments take a while to propagate; without this the first apply often fails with 403s.
resource "time_sleep" "rbac_propagation" {
  create_duration = "60s"

  depends_on = [
    azurerm_role_assignment.deployer_kv_secrets_officer,
    azurerm_role_assignment.apps_kv_secrets_user,
    azurerm_role_assignment.apps_acr_pull,
  ]
}

# --- Generated secrets -------------------------------------------------------
# Rotate any of these with: terraform apply -replace=<resource address> (see docs/deployment.md).

resource "random_password" "delivery_api_key" {
  length  = 40
  special = false
}

resource "random_bytes" "hmac_secret" {
  length = 64 # Umbraco:CMS:Imaging:HMACSecretKey expects base64 of 64 random bytes
}

resource "random_password" "webhook_secret" {
  length  = 40
  special = false
}

resource "random_password" "reindex_secret" {
  length  = 40
  special = false
}

resource "random_password" "meilisearch_master_key" {
  length  = 40
  special = false
}

resource "random_password" "sql_admin" {
  length           = 32
  special          = true
  override_special = "!#%*-_=+"
  min_lower        = 2
  min_upper        = 2
  min_numeric      = 2
  min_special      = 2
}

resource "random_password" "umbraco_admin" {
  length      = 24
  special     = false
  min_lower   = 2
  min_upper   = 2
  min_numeric = 2
}

# Seq: first-run admin password (Seq asks for a new one on first login) and the API key the apps
# send with every log batch (registered in Seq once, see docs/deployment.md).
resource "random_password" "seq_admin" {
  length      = 24
  special     = false
  min_lower   = 2
  min_upper   = 2
  min_numeric = 2
}

resource "random_password" "seq_ingestion_api_key" {
  length  = 32
  special = false
}

# oauth2-proxy's cookie encryption key (16, 24 or 32 bytes).
resource "random_password" "seq_proxy_cookie" {
  length  = 32
  special = false
}

# Encrypts the web's customer session cookie (only with customer sign-in).
resource "random_password" "web_session" {
  count   = local.customer_auth ? 1 : 0
  length  = 48
  special = false
}

locals {
  secrets = {
    "delivery-api-key"                = random_password.delivery_api_key.result
    "hmac-secret"                     = random_bytes.hmac_secret.base64
    "webhook-secret"                  = random_password.webhook_secret.result
    "reindex-secret"                  = random_password.reindex_secret.result
    "meilisearch-master-key"          = random_password.meilisearch_master_key.result
    "meilisearch-connection-string"   = "Endpoint=${local.meilisearch_url};MasterKey=${random_password.meilisearch_master_key.result}"
    "sql-admin-password"              = random_password.sql_admin.result
    "umbraco-db-connection-string"    = "Server=tcp:${azurerm_mssql_server.this.fully_qualified_domain_name},1433;Initial Catalog=${azurerm_mssql_database.umbraco.name};User ID=${var.sql_admin_login};Password=${random_password.sql_admin.result};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
    "umbraco-admin-password"          = random_password.umbraco_admin.result
    "media-storage-connection-string" = azurerm_storage_account.this.primary_connection_string
    "seq-admin-password"              = random_password.seq_admin.result
    "seq-ingestion-api-key"           = random_password.seq_ingestion_api_key.result
    # OIDC client secrets come from the app registrations (identity.tf); the web's only with customer sign-in.
    "cms-oidc-secret"         = azuread_application_password.staff["cms"].value
    "seq-oidc-secret"         = azuread_application_password.staff["seq"].value
    "seq-proxy-cookie-secret" = random_password.seq_proxy_cookie.result
    "web-oidc-secret"         = one(azuread_application_password.web[*].value)
    "web-session-secret"      = one(random_password.web_session[*].result)
  }
  customer_secrets = ["web-oidc-secret", "web-session-secret"]
}

resource "azurerm_key_vault_secret" "this" {
  # Names only (never the sensitive values) decide which secrets exist.
  for_each = setsubtract(keys(local.secrets), local.customer_auth ? [] : local.customer_secrets)

  name         = each.key
  value        = local.secrets[each.key]
  key_vault_id = azurerm_key_vault.this.id

  depends_on = [time_sleep.rbac_propagation]
}
