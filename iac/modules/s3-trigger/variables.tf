variable "prefix" {
  description = "Resource name prefix"
  type        = string
}

variable "bucket_id" {
  description = "ID (name) of the S3 bucket to attach the notification configuration to. This module does not create the bucket — it is owned by iac/modules/media-bucket in this repo's own root config."
  type        = string
}

variable "bucket_arn" {
  description = "ARN of the S3 bucket, used to scope the Lambda invoke permission's source_arn"
  type        = string
}

variable "lambda_function_arn" {
  description = "ARN of the moderation Lambda function to invoke on ObjectCreated (iac/modules/lambda-moderation output)"
  type        = string
}

variable "lambda_function_name" {
  description = "Name of the moderation Lambda function to invoke on ObjectCreated (iac/modules/lambda-moderation output)"
  type        = string
}

# The S3 object-key convention (assets/{ownerId}/{assetId}/{filename}) that
# AssetKeyConventionFilter assumes was confirmed cross-repo with asset-registry-api's
# real S3MediaStorageService (G-001, closed 2026-07-27; see .specs/project/STATE.md).
# No default is hardcoded in this module itself — the calling root module supplies it
# (its own variables.tf defaults to "assets/"), so this module stays reusable if the
# convention ever needs to change without editing this file.
variable "filter_prefix" {
  description = "S3 object key prefix filter for the ObjectCreated notification. No default here — supplied by the calling root module (defaults to \"assets/\" there). Leave empty for no prefix filter."
  type        = string
  default     = ""
}

variable "filter_suffix" {
  description = "S3 object key suffix filter for the ObjectCreated notification. Leave empty for no suffix filter."
  type        = string
  default     = ""
}
