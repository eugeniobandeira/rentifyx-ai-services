variable "prefix" {
  description = "Resource name prefix"
  type        = string
}

variable "media_bucket_arn" {
  description = "ARN of the S3 bucket holding asset media, scoped for the moderation Lambda"
  type        = string
}

variable "moderation_idempotency_table_arn" {
  description = "ARN of the DynamoDB table the moderation Lambda uses to skip re-processing the same S3 object/ETag"
  type        = string
}

variable "moderation_review_queue_arn" {
  description = "ARN of the SQS queue moderation enqueues PendingReview items to (iac/modules/review-queue output)"
  type        = string
}

variable "moderation_failure_dlq_arn" {
  description = "ARN of the SQS DLQ for Rekognition invocations that fail after retries are exhausted (iac/modules/review-queue output)"
  type        = string
}

variable "bedrock_model_arn" {
  description = "ARN of the specific Bedrock model the enrichment Lambda is allowed to invoke"
  type        = string
}

variable "enrichment_idempotency_table_arn" {
  description = "ARN of the DynamoDB table the enrichment Lambda uses to skip re-processing the same assetId (iac/modules/dynamodb-table output table_arn)"
  type        = string
}

variable "enrichment_failure_dlq_arn" {
  description = "ARN of the SQS DLQ enrichment sends to when processing fails after retries are exhausted (iac/modules/review-queue output enrichment_failure_dlq_arn)"
  type        = string
}
