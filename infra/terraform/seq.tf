# --- Seq (log search; UI only through seq-proxy) ---------------------------------------------------
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

  # Internal only: the apps ship logs to https://seq.internal.<domain> (ingestion is locked to the API key once
  # bootstrapped, see the docs); people reach the UI and API through seq-proxy below.
  ingress {
    external_enabled = false
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

# --- seq-proxy (staff sign-in in front of the Seq UI, external) -----------------------------------
#
# oauth2-proxy with generic OIDC against Entra ID (app registration "seq" in identity.tf). It mirrors the AppHost's
# seq-proxy, except: discovery works here (one host for browser and server); no --backend-logout-url (Entra has no
# back-channel GET logout, so sign-out keeps the Entra session); offline_access for --cookie-refresh;
# --reverse-proxy/--pass-host-header=false for the Container Apps ingress; secure cookies. Only the "admin" value of
# the "roles" claim gets in; the session is refreshed (roles re-checked) every 5 minutes and lasts 8 hours. Seq keeps
# its own login behind it. Seq's HTTP API (/api/, also the Seq login) is behind the sign-in too; seqcli and
# infra/seq/alerts.sh run inside the environment against seq_internal_url (docs/deployment.md).

resource "azurerm_container_app" "seq_proxy" {
  name                         = "seq-proxy"
  container_app_environment_id = azurerm_container_app_environment.this.id
  resource_group_name          = azurerm_resource_group.this.name
  revision_mode                = "Single"
  tags                         = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.apps.id]
  }

  dynamic "secret" {
    for_each = toset(["seq-oidc-secret", "seq-proxy-cookie-secret"])
    content {
      name                = secret.value
      identity            = azurerm_user_assigned_identity.apps.id
      key_vault_secret_id = local.kv_secret_ids[secret.value]
    }
  }

  ingress {
    external_enabled = true
    target_port      = 4180
    transport        = "http"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "seq-proxy"
      image  = var.seq_proxy_image
      cpu    = 0.25
      memory = "0.5Gi"
      args = [
        "--http-address=0.0.0.0:4180",
        "--upstream=${local.seq_ingest_url}",
        "--pass-host-header=false", # the environment's ingress routes the upstream call by its Host
        "--reverse-proxy=true",     # behind the Container Apps ingress
        "--provider=oidc",
        "--client-id=${azuread_application.staff["seq"].client_id}",
        "--oidc-issuer-url=${local.staff_authority}",
        "--scope=openid email profile offline_access", # offline_access: refresh tokens for --cookie-refresh
        "--code-challenge-method=S256",
        "--redirect-url=${local.seq_url}/oauth2/callback",
        "--oidc-groups-claim=roles",
        "--allowed-group=admin",
        "--email-domain=*",
        "--skip-provider-button",
        "--cookie-name=_seq_proxy",
        "--cookie-secure=true",
        "--cookie-expire=8h",
        "--cookie-refresh=5m",
        "--skip-auth-route=GET=^/health$",
      ]

      env {
        name        = "OAUTH2_PROXY_CLIENT_SECRET"
        secret_name = "seq-oidc-secret"
      }
      env {
        name        = "OAUTH2_PROXY_COOKIE_SECRET"
        secret_name = "seq-proxy-cookie-secret"
      }

      liveness_probe {
        transport = "HTTP"
        port      = 4180
        path      = "/ping"
      }
    }
  }

  depends_on = [time_sleep.rbac_propagation, azurerm_container_app.seq]
}
