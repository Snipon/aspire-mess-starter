resource "azurerm_mssql_server" "this" {
  name                          = "sql-${local.unique}"
  location                      = azurerm_resource_group.this.location
  resource_group_name           = azurerm_resource_group.this.name
  version                       = "12.0"
  administrator_login           = var.sql_admin_login
  administrator_login_password  = random_password.sql_admin.result
  minimum_tls_version           = "1.2"
  public_network_access_enabled = true
  tags                          = var.tags
}

# Container Apps on the consumption plan have no fixed outbound IP, so allow Azure services.
# Use a VNet-integrated environment + private endpoint for stricter isolation.
resource "azurerm_mssql_firewall_rule" "azure_services" {
  name             = "AllowAzureServices"
  server_id        = azurerm_mssql_server.this.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

resource "azurerm_mssql_database" "umbraco" {
  name      = "umbraco"
  server_id = azurerm_mssql_server.this.id
  sku_name  = var.sql_database_sku
  collation = "SQL_Latin1_General_CP1_CI_AS"
  tags      = var.tags
}
