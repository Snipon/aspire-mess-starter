terraform {
  required_version = ">= 1.9"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.81"
    }
    # Entra ID / External ID app registrations (identity.tf).
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 3.10"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.7"
    }
    time = {
      source  = "hashicorp/time"
      version = "~> 0.13"
    }
    # Resources azurerm does not model yet (Container Apps Aspire dashboard component).
    azapi = {
      source  = "Azure/azapi"
      version = "~> 2.13"
    }
  }

  # Configure a remote backend for shared use, e.g.:
  # backend "azurerm" {
  #   resource_group_name  = "tfstate"
  #   storage_account_name = "<state account>"
  #   container_name       = "tfstate"
  #   key                  = "aspire-test.tfstate"
  # }
}

provider "azurerm" {
  features {
    key_vault {
      # Keep soft-deleted vaults recoverable instead of purging them on destroy.
      purge_soft_delete_on_destroy = false
    }
  }

  subscription_id = var.subscription_id
}

provider "azapi" {
  subscription_id = var.subscription_id
}

# Staff app registrations: the tenant Terraform signs in to (az login / ARM_TENANT_ID).
provider "azuread" {}

# Customer app registrations: the Entra External ID tenant. Only used when customer_tenant_id is set; sign in to it
# first (az login --tenant <id> --allow-no-subscriptions).
provider "azuread" {
  alias     = "customers"
  tenant_id = var.customer_tenant_id != "" ? var.customer_tenant_id : null
}
