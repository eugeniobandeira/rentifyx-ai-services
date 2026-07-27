variable "prefix" {
  description = "Resource name prefix"
  type        = string
}

variable "dedupe_role_arn" {
  description = "ARN of the dedupe Lambda's IAM role (iac/modules/iam-roles output dedupe_role_arn) - ADR-AI-002, no shared execution role"
  type        = string
}

variable "lambda_package_path" {
  description = "Path to the built deployment package (zip) for the dedupe Lambda, produced by `dotnet lambda package` / the CI build step"
  type        = string
}

variable "lambda_handler" {
  description = "Lambda handler string, ASSEMBLY::NAMESPACE.TYPE::METHOD"
  type        = string
  default     = "RentifyxAiServices.Dedupe::RentifyxAiServices.Dedupe.DedupeHandler::FunctionHandler"
}

variable "lambda_runtime" {
  description = "Lambda runtime identifier - managed .NET runtime zip, not Native AOT (ADR-AI-001)"
  type        = string
  default     = "dotnet10"
}

variable "timeout" {
  description = "Lambda timeout in seconds"
  type        = number
  default     = 30
}

variable "memory_size" {
  description = "Lambda memory size in MB"
  type        = number
  default     = 512
}

variable "idempotency_table_name" {
  description = "Name of the DynamoDB table the dedupe Lambda uses to skip re-processing the same S3 object/ETag (injected as IDEMPOTENCY_TABLE_NAME)"
  type        = string
}

variable "hash_table_name" {
  description = "Name of the DynamoDB table mapping perceptual image hash to the first asset that produced it (injected as HASH_TABLE_NAME)"
  type        = string
}

variable "failure_dlq_url" {
  description = "URL of the DLQ for S3 read or hash-store failures that exhaust retries (iac/modules/review-queue output dedupe_failure_dlq_url), injected as FAILURE_DLQ_URL"
  type        = string
}

variable "kafka_duplicate_detected_topic" {
  description = "Kafka topic dedupe publishes AssetDuplicateDetected events to, injected as KAFKA_DUPLICATE_DETECTED_TOPIC"
  type        = string
  default     = "asset-duplicate-detected"
}
