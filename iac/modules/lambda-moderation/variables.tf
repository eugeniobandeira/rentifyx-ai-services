variable "prefix" {
  description = "Resource name prefix"
  type        = string
}

variable "moderation_role_arn" {
  description = "ARN of the moderation Lambda's IAM role (iac/modules/iam-roles output moderation_role_arn) - ADR-AI-002, no shared execution role"
  type        = string
}

variable "lambda_package_path" {
  description = "Path to the built deployment package (zip) for the moderation Lambda, produced by `dotnet lambda package` / the CI build step"
  type        = string
}

variable "lambda_handler" {
  description = "Lambda handler string, ASSEMBLY::NAMESPACE.TYPE::METHOD"
  type        = string
  default     = "RentifyxAiServices.Moderation::RentifyxAiServices.Moderation.ModerationHandler::FunctionHandler"
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

# --- Idempotency (DynamoDB) ----------------------------------------------
#
# No DynamoDB table resource/module exists anywhere in this repo's iac/ yet
# (tracked in .specs/project/STATE.md Open Items) - the table name is taken
# as an input here rather than this module inventing its own table schema.

variable "idempotency_table_name" {
  description = "Name of the DynamoDB table the moderation Lambda uses to skip re-processing the same S3 object/ETag (injected as IDEMPOTENCY_TABLE_NAME). No table resource exists yet in this repo's iac/ - see .specs/project/STATE.md Open Items."
  type        = string
}

# --- Review queue / DLQ (iac/modules/review-queue outputs) ---------------

variable "review_queue_url" {
  description = "URL of the manual review SQS queue (iac/modules/review-queue output review_queue_url), injected as REVIEW_QUEUE_URL"
  type        = string
}

variable "failure_dlq_url" {
  description = "URL of the DLQ for Rekognition invocations that fail after retries are exhausted (iac/modules/review-queue module owns aws_sqs_queue.moderation_failure_dlq but does not currently output its URL, only its ARN as moderation_failure_dlq_arn) - passed through here as-is, injected as FAILURE_DLQ_URL"
  type        = string
}

# --- Kafka topics (published via KafkaModerationEventPublisher, ADR-AI-004) ---

variable "kafka_moderated_topic" {
  description = "Kafka topic moderation publishes AssetMediaModerated events to, injected as KAFKA_MODERATED_TOPIC"
  type        = string
  default     = "asset-media-moderated"
}

variable "kafka_pending_review_topic" {
  description = "Kafka topic moderation publishes AssetPendingManualReview events to, injected as KAFKA_PENDING_REVIEW_TOPIC"
  type        = string
  default     = "asset-pending-manual-review"
}
