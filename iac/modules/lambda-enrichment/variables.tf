variable "prefix" {
  description = "Resource name prefix"
  type        = string
}

variable "enrichment_role_arn" {
  description = "ARN of the enrichment Lambda's IAM role (iac/modules/iam-roles output enrichment_role_arn) - ADR-AI-002, no shared execution role"
  type        = string
}

variable "lambda_package_path" {
  description = "Path to the built deployment package (zip) for the enrichment Lambda, produced by `dotnet lambda package` / the CI build step"
  type        = string
}

variable "lambda_handler" {
  description = "Lambda handler string, ASSEMBLY::NAMESPACE.TYPE::METHOD"
  type        = string
  default     = "RentifyxAiServices.Enrichment::RentifyxAiServices.Enrichment.EnrichmentHandler::FunctionHandler"
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

variable "idempotency_table_name" {
  description = "Name of the DynamoDB table the enrichment Lambda uses to skip re-processing the same asset (injected as ENRICHMENT_IDEMPOTENCY_TABLE_NAME), keyed enrichment:{assetId} - a separate table from Moderation's. Comes from iac/modules/dynamodb-table output table_name."
  type        = string
}

# --- Failure DLQ (iac/modules/review-queue output) ------------------------

variable "failure_dlq_url" {
  description = "URL of the DLQ for enrichment failures (Bedrock invocation errors, Kafka publish errors, etc. after retries are exhausted), injected as ENRICHMENT_FAILURE_DLQ_URL. Comes from iac/modules/review-queue output enrichment_failure_dlq_url."
  type        = string
}

# --- Bedrock -----------------------------------------------------------

variable "bedrock_region" {
  description = "AWS region for the Bedrock Runtime Converse API client, injected as BEDROCK_REGION"
  type        = string
  default     = "us-east-1"
}

# --- Kafka topic (published via KafkaEnrichmentEventPublisher, ADR-AI-005/006) ---

variable "kafka_enrichment_suggested_topic" {
  description = "Kafka topic enrichment publishes AssetEnrichmentSuggested events to, injected as KAFKA_ENRICHMENT_SUGGESTED_TOPIC"
  type        = string
  default     = "asset-enrichment-suggested"
}
