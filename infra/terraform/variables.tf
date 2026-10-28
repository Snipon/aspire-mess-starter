variable "subscription_id" {
  description = "Azure subscription ID to deploy into. Leave null to use ARM_SUBSCRIPTION_ID or the Azure CLI default."
  type        = string
  default     = null
}

variable "name" {
  description = "Short, lowercase alphanumeric prefix used for resource names (globally unique names get a random suffix)."
  type        = string
  default     = "aspiretest"

  validation {
    condition     = can(regex("^[a-z][a-z0-9]{2,11}$", var.name))
    error_message = "name must be 3-12 lowercase alphanumeric characters starting with a letter."
  }
}

variable "location" {
  description = "Azure region for all resources."
  type        = string
  default     = "swedencentral"
}

variable "tags" {
  description = "Tags applied to every resource."
  type        = map(string)
  default     = {}
}

variable "image_tag" {
  description = "Tag of the cms, api and web images in the container registry (e.g. a git SHA)."
  type        = string
  default     = "latest"
}

variable "meilisearch_image" {
  description = "Meilisearch container image (public). Keep in step with the version used by the Aspire AppHost."
  type        = string
  default     = "docker.io/getmeili/meilisearch:v1.54.0"
}

variable "umbraco_admin_email" {
  description = "E-mail of the Umbraco administrator created by the unattended install on first start."
  type        = string
  default     = "admin@example.com"
}

variable "sql_database_sku" {
  description = "SKU of the Azure SQL database that holds the Umbraco schema (e.g. Basic, S0, GP_S_Gen5_1)."
  type        = string
  default     = "S0"
}

variable "sql_admin_login" {
  description = "Administrator login of the Azure SQL server (password is generated and stored in Key Vault)."
  type        = string
  default     = "umbracoadmin"
}

variable "log_retention_days" {
  description = "Log Analytics retention in days."
  type        = number
  default     = 30
}
