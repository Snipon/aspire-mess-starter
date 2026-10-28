terraform {
  required_version = ">= 1.9"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.81"
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
