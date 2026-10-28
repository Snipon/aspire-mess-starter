resource "azurerm_storage_account" "this" {
  name                            = "st${local.unique}"
  location                        = azurerm_resource_group.this.location
  resource_group_name             = azurerm_resource_group.this.name
  account_tier                    = "Standard"
  account_replication_type        = "LRS"
  min_tls_version                 = "TLS1_2"
  allow_nested_items_to_be_public = false
  tags                            = var.tags
}

# Umbraco media (Umbraco.StorageProviders.AzureBlob). Private: media is served through the cms app,
# which also enforces the ImageSharp HMAC signature on resize requests.
resource "azurerm_storage_container" "media" {
  name                  = "media"
  storage_account_id    = azurerm_storage_account.this.id
  container_access_type = "private"
}
