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

# --- observability -------------------------------------------------------------------------------

variable "aspire_dashboard_user_object_ids" {
  description = "Entra ID object IDs of users who get Contributor on the Container Apps environment to open the Aspire dashboard. Must be users: the dashboard does not honour group-based role assignments."
  type        = list(string)
  default     = []
}

variable "alert_email_addresses" {
  description = "E-mail addresses notified by the Azure Monitor action group (availability, Container Apps and SQL alerts)."
  type        = list(string)
  default     = []
}

variable "alert_webhook_url" {
  description = "Optional webhook (e.g. a Teams/Slack workflow or Logic App) that receives Azure Monitor alerts in the common alert schema."
  type        = string
  default     = null
  sensitive   = true
}

variable "availability_test_locations" {
  description = "Application Insights availability test locations (at least 5 recommended). The alert fires when all but two fail."
  type        = list(string)
  default     = ["emea-nl-ams-azr", "emea-gb-db3-azr", "emea-fr-pra-edge", "emea-ru-msa-edge", "us-va-ash-azr"]

  validation {
    condition     = length(var.availability_test_locations) >= 3
    error_message = "Use at least 3 availability test locations."
  }
}

variable "http_5xx_alert_threshold" {
  description = "Number of 5xx responses per app within 5 minutes that raises an alert."
  type        = number
  default     = 10
}

variable "seq_image" {
  description = "Seq container image. Keep in step with the version used by the Aspire AppHost (Aspire.Hosting.Seq)."
  type        = string
  default     = "docker.io/datalust/seq:2025.2"
}

variable "seq_storage_quota_gb" {
  description = "Size of the Premium Azure Files share holding Seq's data (Premium shares start at 100 GiB)."
  type        = number
  default     = 100
}

variable "seq_retention" {
  description = "How long Seq keeps events (e.g. 30d). Applied by infra/seq/alerts.sh (SEQ_RETENTION), which reads this output."
  type        = string
  default     = "30d"
}
