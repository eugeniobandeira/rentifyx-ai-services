variable "aws_region" {
  description = "AWS region to deploy resources"
  type        = string
  default     = "sa-east-1"
}

variable "environment" {
  description = "Deployment environment (production, staging, development)"
  type        = string
  default     = "production"
}

variable "app_name" {
  description = "Application name used as resource name prefix"
  type        = string
  default     = "rentifyx"
}

# --- Asset media S3 bucket -------------------------------------------------
#
# No repo (this one, asset-registry-api, or platform) currently provisions
# this bucket in Terraform - it doesn't exist yet anywhere. No default here
# on purpose; the real values must be supplied once the bucket is created and
# the S3 key convention is confirmed with asset-registry-api (gap G-001, see
# .specs/project/STATE.md Open Items).

variable "media_bucket_id" {
  description = "ID (name) of the S3 bucket holding asset media - not yet provisioned anywhere, no default"
  type        = string
}

variable "media_bucket_arn" {
  description = "ARN of the S3 bucket holding asset media - not yet provisioned anywhere, no default"
  type        = string
}

variable "filter_prefix" {
  description = "S3 object key prefix filter for the moderation trigger - deliberately no default convention baked in (G-001 unconfirmed cross-repo). Leave empty for no prefix filter."
  type        = string
  default     = ""
}

variable "filter_suffix" {
  description = "S3 object key suffix filter for the moderation trigger. Leave empty for no suffix filter."
  type        = string
  default     = ""
}

# --- Moderation Lambda deployment ------------------------------------------

variable "lambda_package_path" {
  description = "Path to the built deployment package (zip) for the moderation Lambda, produced by `dotnet lambda package` / the CI build step - no default, must be supplied at apply time"
  type        = string
}

# --- Idempotency (DynamoDB) -------------------------------------------------
#
# No DynamoDB table resource/module exists anywhere in this repo's iac/ yet
# (tracked in .specs/project/STATE.md Open Items) - name and ARN are taken as
# plain inputs here rather than this config inventing a table schema.

variable "idempotency_table_name" {
  description = "Name of the DynamoDB table the moderation Lambda uses to skip re-processing the same S3 object/ETag - no table resource exists yet, see .specs/project/STATE.md Open Items"
  type        = string
}

variable "idempotency_table_arn" {
  description = "ARN of the same DynamoDB idempotency table, scoped into the moderation IAM policy - no table resource exists yet"
  type        = string
}

# --- Bedrock (Enrichment) ---------------------------------------------------
#
# Enrichment (E-03/E-04) isn't implemented yet, but iac/modules/iam-roles
# already scopes an enrichment role ahead of it - no default, the real model
# ARN is a decision that belongs to E-03's design, not this root config.

variable "bedrock_model_arn" {
  description = "ARN of the specific Bedrock model the enrichment Lambda is allowed to invoke - no default, decided when E-03 lands"
  type        = string
}
