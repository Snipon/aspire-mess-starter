output "resource_group_name" {
  description = "Resource group containing all resources."
  value       = azurerm_resource_group.this.name
}

output "acr_login_server" {
  description = "Container registry to push the cms, api and web images to."
  value       = azurerm_container_registry.this.login_server
}

output "acr_name" {
  description = "Container registry name (for az acr login --name)."
  value       = azurerm_container_registry.this.name
}

output "key_vault_name" {
  description = "Key Vault holding all generated secrets."
  value       = azurerm_key_vault.this.name
}

output "web_url" {
  description = "Public URL of the Astro site."
  value       = local.web_url
}

output "api_url" {
  description = "Public URL of the API (OpenAPI at /openapi/v1.json, Scalar at /scalar)."
  value       = local.api_url
}

output "cms_url" {
  description = "Public URL of Umbraco (backoffice at /umbraco)."
  value       = local.cms_url
}

output "umbraco_admin_email" {
  description = "Umbraco administrator login; the password is the 'umbraco-admin-password' Key Vault secret."
  value       = var.umbraco_admin_email
}

output "sql_server_fqdn" {
  description = "Azure SQL server hosting the umbraco database."
  value       = azurerm_mssql_server.this.fully_qualified_domain_name
}

output "aspire_dashboard_url" {
  description = "Aspire dashboard of the Container Apps environment (needs Contributor on the environment, see aspire_dashboard_user_object_ids)."
  value       = "https://${azapi_resource.aspire_dashboard.name}.ext.${azurerm_container_app_environment.this.default_domain}"
}

output "seq_url" {
  description = "Seq UI through seq-proxy: Entra ID sign-in (app role admin), then Seq's own login (user admin; first-run password is the 'seq-admin-password' Key Vault secret)."
  value       = local.seq_url
}

output "staff_app_client_ids" {
  description = "Client IDs of the staff app registrations in Entra ID (assign app roles admin/editor in Enterprise applications)."
  value       = { for k, app in azuread_application.staff : k => app.client_id }
}

output "customer_app_client_ids" {
  description = "Client IDs of the web and API app registrations in the Entra External ID tenant (empty without customer sign-in)."
  value       = local.customer_auth ? { web = local.customer_web_id, shop_api = local.customer_api_id } : {}
}

output "seq_retention" {
  description = "Seq retention for infra/seq/alerts.sh (SEQ_RETENTION)."
  value       = var.seq_retention
}
