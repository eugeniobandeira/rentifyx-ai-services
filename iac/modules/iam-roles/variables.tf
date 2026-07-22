variable "prefix" {
  description = "Resource name prefix"
  type        = string
}

variable "media_bucket_arn" {
  description = "ARN of the S3 bucket holding asset media, scoped for the moderation Lambda"
  type        = string
}

variable "bedrock_model_arn" {
  description = "ARN of the specific Bedrock model the enrichment Lambda is allowed to invoke"
  type        = string
}
