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
# Provisioned by iac/modules/media-bucket (E-03c) - domain-specific to this
# repo, not rentifyx-platform's shared cross-domain infra. See main.tf's
# module.media_bucket.

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
# Provisioned by iac/modules/dynamodb-table (E-03c adopts the same generic
# module E-03b built for Enrichment) - see main.tf's
# module.moderation_idempotency_table.

# --- Bedrock (Enrichment) ---------------------------------------------------
#
# Enrichment (E-03/E-04) isn't implemented yet, but iac/modules/iam-roles
# already scopes an enrichment role ahead of it - no default, the real model
# ARN is a decision that belongs to E-03's design, not this root config.

variable "bedrock_model_arn" {
  description = "ARN of the specific Bedrock model the enrichment Lambda is allowed to invoke. Confirmed real via `aws bedrock list-inference-profiles` against account 166613156216 (2026-07-24): the us.anthropic.claude-sonnet-5 cross-region inference profile only routes to us-east-1/us-east-2/us-west-2 - Claude Sonnet 5 has no sa-east-1 presence, so this stays us-east-1 even though every other resource in this config defaults to sa-east-1 (BEDROCK_REGION env var on lambda-enrichment matches)."
  type        = string
  default     = "arn:aws:bedrock:us-east-1:166613156216:inference-profile/us.anthropic.claude-sonnet-5"
}

# --- Enrichment Lambda deployment (E-03b) -----------------------------------

variable "enrichment_lambda_package_path" {
  description = "Path to the built deployment package (zip) for the enrichment Lambda, produced by `dotnet lambda package` / the CI build step - separate package from moderation's, no default, must be supplied at apply time"
  type        = string
}
