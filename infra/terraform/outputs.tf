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
