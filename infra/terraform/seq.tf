# --- Seq (log search, external UI) ----------------------------------------------------------------
#
# Datalust only supports Seq on local-disk-equivalent storage: Azure Disks or *Premium* Azure Files
# (standard Azure Files leads to storage corruption). Container Apps cannot mount Azure Disks, so Seq
# keeps its data on a Premium (FileStorage) SMB share. It runs exactly one replica; see
# docs/deployment.md for the revision-overlap caveat and when to move Seq to a VM/ACI instead.

resource "azurerm_storage_account" "seq" {
  name                     = "stseq${local.unique}"
  location                 = azurerm_resource_group.this.location
  resource_group_name      = azurerm_resource_group.this.name
  account_kind             = "FileStorage"
  account_tier             = "Premium"
  account_replication_type = "LRS"
  min_tls_version          = "TLS1_2"
  tags                     = var.tags
}

resource "azurerm_storage_share" "seq" {
  name               = "seq-data"
  storage_account_id = azurerm_storage_account.seq.id
  quota              = var.seq_storage_quota_gb
}

resource "azurerm_container_app_environment_storage" "seq" {
  name                         = "seq-data"
  container_app_environment_id = azurerm_container_app_environment.this.id
  account_name                 = azurerm_storage_account.seq.name
  access_key                   = azurerm_storage_account.seq.primary_access_key
  share_name                   = azurerm_storage_share.seq.name
  access_mode                  = "ReadWrite"
}

resource "azurerm_container_app" "seq" {
  name                         = "seq"
  container_app_environment_id = azurerm_container_app_environment.this.id
  resource_group_name          = azurerm_resource_group.this.name
  revision_mode                = "Single"
  tags                         = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.apps.id]
  }

  secret {
    name                = "seq-admin-password"
    identity            = azurerm_user_assigned_identity.apps.id
    key_vault_secret_id = local.kv_secret_ids["seq-admin-password"]
  }

  # External: the Seq UI (login required). Apps ship logs over the same ingress by FQDN, which stays
  # inside the environment; ingestion is locked to the API key once bootstrapped (see the docs).
  ingress {
    external_enabled = true
    target_port      = 80
    transport        = "http"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    # Never more than one process on the data share.
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "seq"
      image  = var.seq_image
      cpu    = 1
      memory = "2Gi"

      env {
        name  = "ACCEPT_EULA"
        value = "Y"
      }
      env {
        # Only used on the very first start (empty data share); Seq then asks for a new password.
        name        = "SEQ_FIRSTRUN_ADMINPASSWORD"
        secret_name = "seq-admin-password"
      }
      env {
        # Links in alert notifications point at the public URL.
        name  = "SEQ_API_CANONICALURI"
        value = local.seq_url
      }

      volume_mounts {
        name = "data"
        path = "/data"
      }
    }

    volume {
      name         = "data"
      storage_type = "AzureFile"
      storage_name = azurerm_container_app_environment_storage.seq.name
    }
  }

  depends_on = [time_sleep.rbac_propagation]
}
