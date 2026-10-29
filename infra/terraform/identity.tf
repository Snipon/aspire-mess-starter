# --- Identity providers (OpenID Connect) -----------------------------------------------------------
#
# Production counterpart of the local Keycloak realms (infra/keycloak). The apps only see generic OIDC settings, so
# these app registrations reproduce what the realms promise: a "roles" claim with admin/editor for staff, and customer
# access tokens for the API's audience.
#
# Staff: Microsoft Entra ID, the tenant Terraform runs in. One app registration per client (cms backoffice, Seq proxy)
# with the app roles admin and editor; only assigned users may sign in. Drupal Commerce is not deployed to Azure yet,
# so it has no app registration (see docs/deployment.md).
#
# Customers: Microsoft Entra External ID (CIAM), a separate tenant that Terraform cannot create. Off unless
# customer_tenant_id is set: then the web runs without customer sign-in and the API treats everyone as a guest.

locals {
  staff_authority = "https://login.microsoftonline.com/${data.azuread_client_config.staff.tenant_id}/v2.0"

  # Redirect URIs per staff client (the same paths as the Keycloak staff realm).
  staff_apps = {
    cms = ["${local.cms_url}/umbraco-signin-oidc"]
    seq = ["${local.seq_url}/oauth2/callback"]
  }
  staff_roles = {
    admin  = "Administrators: full access (Umbraco admin, Seq)."
    editor = "Editors: Umbraco content editing."
  }
  staff_role_assignments = merge(
    { for p in setproduct(keys(local.staff_apps), var.staff_admin_object_ids) : "${p[0]}/admin/${p[1]}" => { app = p[0], role = "admin", principal = p[1] } },
    { for p in setproduct(keys(local.staff_apps), var.staff_editor_object_ids) : "${p[0]}/editor/${p[1]}" => { app = p[0], role = "editor", principal = p[1] } },
  )

  customer_auth      = var.customer_tenant_id != ""
  customer_authority = var.customer_authority != "" ? var.customer_authority : "https://${var.customer_tenant_id}.ciamlogin.com/${var.customer_tenant_id}/v2.0"
  customer_api_id    = one(azuread_application.shop_api[*].client_id)
  customer_web_id    = one(azuread_application.web[*].client_id)

  # Sign-in scopes granted to every client up front, so nobody sees a consent prompt.
  oidc_scopes = ["openid", "profile", "email", "offline_access"]
}

data "azuread_client_config" "staff" {}

# Microsoft Graph holds the openid/profile/email/offline_access scopes.
data "azuread_service_principal" "graph" {
  client_id = "00000003-0000-0000-c000-000000000000"
}

# --- staff (Entra ID) ------------------------------------------------------------------------------

resource "random_uuid" "staff_role" {
  for_each = local.staff_roles
}

resource "azuread_application" "staff" {
  for_each = local.staff_apps

  display_name     = "${var.name}-${each.key}"
  sign_in_audience = "AzureADMyOrg"
  owners           = [data.azuread_client_config.staff.object_id]

  web {
    redirect_uris = each.value
  }

  # Assigned app roles arrive in the ID and access tokens as "roles", like the Keycloak realm's roles claim.
  dynamic "app_role" {
    for_each = local.staff_roles
    content {
      allowed_member_types = ["User"]
      description          = app_role.value
      display_name         = app_role.key
      enabled              = true
      id                   = random_uuid.staff_role[app_role.key].result
      value                = app_role.key
    }
  }

  # Entra sends neither email nor email_verified by default. xms_edov is true when the email's domain is verified by
  # the tenant (the cms links existing users only then).
  optional_claims {
    id_token {
      name = "email"
    }
    id_token {
      name = "xms_edov"
    }
  }

  required_resource_access {
    resource_app_id = data.azuread_service_principal.graph.client_id

    dynamic "resource_access" {
      for_each = local.oidc_scopes
      content {
        id   = data.azuread_service_principal.graph.oauth2_permission_scope_ids[resource_access.value]
        type = "Scope"
      }
    }
  }
}

resource "azuread_service_principal" "staff" {
  for_each = local.staff_apps

  client_id                    = azuread_application.staff[each.key].client_id
  app_role_assignment_required = true # only users (or groups) assigned an app role can sign in
  owners                       = [data.azuread_client_config.staff.object_id]
}

resource "azuread_service_principal_delegated_permission_grant" "staff" {
  for_each = local.staff_apps

  service_principal_object_id          = azuread_service_principal.staff[each.key].object_id
  resource_service_principal_object_id = data.azuread_service_principal.graph.object_id
  claim_values                         = local.oidc_scopes
}

resource "azuread_app_role_assignment" "staff" {
  for_each = local.staff_role_assignments

  app_role_id         = random_uuid.staff_role[each.value.role].result
  principal_object_id = each.value.principal
  resource_object_id  = azuread_service_principal.staff[each.value.app].object_id
}

# Default lifetime two years; rotate with terraform apply -replace='azuread_application_password.staff["cms"]'.
resource "azuread_application_password" "staff" {
  for_each = local.staff_apps

  application_id = azuread_application.staff[each.key].id
  display_name   = "terraform"
}

# --- customers (Entra External ID) -----------------------------------------------------------------

data "azuread_service_principal" "customer_graph" {
  count    = local.customer_auth ? 1 : 0
  provider = azuread.customers

  client_id = "00000003-0000-0000-c000-000000000000"
}

resource "random_uuid" "shop_api_scope" {
  count = local.customer_auth ? 1 : 0
}

# The API: exposes the "access" scope the web requests, so customer access tokens carry its client id as aud.
resource "azuread_application" "shop_api" {
  count    = local.customer_auth ? 1 : 0
  provider = azuread.customers

  display_name     = "${var.name}-shop-api"
  sign_in_audience = "AzureADMyOrg"

  api {
    requested_access_token_version = 2 # aud = client id (CustomerAuth__Audience)

    oauth2_permission_scope {
      admin_consent_description  = "Call the shop API as the signed-in customer."
      admin_consent_display_name = "Access the shop API"
      enabled                    = true
      id                         = random_uuid.shop_api_scope[0].result
      type                       = "User"
      value                      = "access"
    }
  }

  optional_claims {
    access_token {
      name = "email"
    }
  }
}

resource "azuread_application_identifier_uri" "shop_api" {
  count    = local.customer_auth ? 1 : 0
  provider = azuread.customers

  application_id = azuread_application.shop_api[0].id
  identifier_uri = "api://${azuread_application.shop_api[0].client_id}"
}

resource "azuread_service_principal" "shop_api" {
  count    = local.customer_auth ? 1 : 0
  provider = azuread.customers

  client_id = azuread_application.shop_api[0].client_id
}

# The web: confidential client (code flow with PKCE). Its home page is also the post-logout redirect.
resource "azuread_application" "web" {
  count    = local.customer_auth ? 1 : 0
  provider = azuread.customers

  display_name     = "${var.name}-web"
  sign_in_audience = "AzureADMyOrg"

  web {
    redirect_uris = ["${local.web_url}/auth/callback", "${local.web_url}/"]
  }

  optional_claims {
    id_token {
      name = "email"
    }
  }

  required_resource_access {
    resource_app_id = data.azuread_service_principal.customer_graph[0].client_id

    dynamic "resource_access" {
      for_each = local.oidc_scopes
      content {
        id   = data.azuread_service_principal.customer_graph[0].oauth2_permission_scope_ids[resource_access.value]
        type = "Scope"
      }
    }
  }

  required_resource_access {
    resource_app_id = azuread_application.shop_api[0].client_id

    resource_access {
      id   = random_uuid.shop_api_scope[0].result
      type = "Scope"
    }
  }
}

resource "azuread_service_principal" "web" {
  count    = local.customer_auth ? 1 : 0
  provider = azuread.customers

  client_id = azuread_application.web[0].client_id
}

# Customers cannot consent themselves: grant the web's scopes for everyone.
resource "azuread_service_principal_delegated_permission_grant" "web_graph" {
  count    = local.customer_auth ? 1 : 0
  provider = azuread.customers

  service_principal_object_id          = azuread_service_principal.web[0].object_id
  resource_service_principal_object_id = data.azuread_service_principal.customer_graph[0].object_id
  claim_values                         = local.oidc_scopes
}

resource "azuread_service_principal_delegated_permission_grant" "web_shop_api" {
  count    = local.customer_auth ? 1 : 0
  provider = azuread.customers

  service_principal_object_id          = azuread_service_principal.web[0].object_id
  resource_service_principal_object_id = azuread_service_principal.shop_api[0].object_id
  claim_values                         = ["access"]
}

# Rotate with terraform apply -replace='azuread_application_password.web[0]' (signs nobody out).
resource "azuread_application_password" "web" {
  count    = local.customer_auth ? 1 : 0
  provider = azuread.customers

  application_id = azuread_application.web[0].id
  display_name   = "terraform"
}
