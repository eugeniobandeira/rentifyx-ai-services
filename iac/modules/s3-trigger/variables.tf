variable "prefix" {
  description = "Resource name prefix"
  type        = string
}

variable "bucket_id" {
  description = "ID (name) of the S3 bucket to attach the notification configuration to. This module does not create the bucket — it is owned elsewhere (likely rentifyx-platform or a not-yet-written root config)."
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
# AssetKeyConventionFilter assumes is UNCONFIRMED cross-repo with asset-registry-api
# (tracked as gap G-001, see .specs/codebase/INTEGRATIONS.md and .specs/project/STATE.md
# Open Items). No default/convention is hardcoded here — the calling root module must
# supply the real prefix/suffix once confirmed. An empty string means "no filter".
variable "filter_prefix" {
  description = "S3 object key prefix filter for the ObjectCreated notification. No default convention is baked in here (G-001 unconfirmed) — supply the real value from the root module, or leave empty for no prefix filter."
  type        = string
  default     = ""
}

variable "filter_suffix" {
  description = "S3 object key suffix filter for the ObjectCreated notification. Leave empty for no suffix filter."
  type        = string
  default     = ""
}
