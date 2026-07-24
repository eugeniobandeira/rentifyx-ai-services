locals {
  prefix = "${var.app_name}-${var.environment}"

  common_tags = {
    Application = var.app_name
    Environment = var.environment
    ManagedBy   = "terraform"
  }
}

provider "aws" {
  region  = var.aws_region
  profile = "rentifyx-admin"

  default_tags {
    tags = local.common_tags
  }
}

# One dedicated IAM role + policy per Lambda, zero permission overlap
# (ADR-AI-002) - moderation, enrichment, dedupe.
module "iam_roles" {
  source = "../modules/iam-roles"

  prefix                           = local.prefix
  media_bucket_arn                 = var.media_bucket_arn
  moderation_idempotency_table_arn = var.idempotency_table_arn
  moderation_review_queue_arn      = module.review_queue.review_queue_arn
  moderation_failure_dlq_arn       = module.review_queue.moderation_failure_dlq_arn
  bedrock_model_arn                = var.bedrock_model_arn
  enrichment_idempotency_table_arn = module.enrichment_idempotency_table.table_arn
  enrichment_failure_dlq_arn       = module.review_queue.enrichment_failure_dlq_arn
}

# Manual-review SQS queue + its DLQ, and the separate Rekognition-failure DLQ
# (MOD-04).
module "review_queue" {
  source = "../modules/review-queue"

  prefix = local.prefix
}

# Moderation Lambda function (E-02) - VPC-attached to reach rentifyx-platform's
# self-hosted Kafka broker, reads that repo's state itself via
# terraform_remote_state (see iac/modules/lambda-moderation/main.tf).
module "lambda_moderation" {
  source = "../modules/lambda-moderation"

  prefix                 = local.prefix
  moderation_role_arn    = module.iam_roles.moderation_role_arn
  lambda_package_path    = var.lambda_package_path
  idempotency_table_name = var.idempotency_table_name
  review_queue_url       = module.review_queue.review_queue_url
  failure_dlq_url        = module.review_queue.moderation_failure_dlq_url
}

# S3 ObjectCreated notification + aws_lambda_permission wiring the bucket to
# the moderation Lambda. filter_prefix/filter_suffix default to "" (no
# convention baked in - G-001 unconfirmed cross-repo with asset-registry-api).
module "s3_trigger" {
  source = "../modules/s3-trigger"

  prefix               = local.prefix
  bucket_id            = var.media_bucket_id
  bucket_arn           = var.media_bucket_arn
  lambda_function_arn  = module.lambda_moderation.function_arn
  lambda_function_name = module.lambda_moderation.function_name
  filter_prefix        = var.filter_prefix
  filter_suffix        = var.filter_suffix
}

# Enrichment's own idempotency table (E-03b) - generic single-partition-key
# module, reusable by Moderation's still-unbacked idempotency table later
# (out of scope here, see .specs/features/e03b-enrichment-iac/spec.md).
module "enrichment_idempotency_table" {
  source = "../modules/dynamodb-table"

  table_name = "${local.prefix}-enrichment-idempotency"
}

# Enrichment Lambda function (E-03) - VPC-attached to reach rentifyx-platform's
# self-hosted Kafka broker, reads that repo's state itself via
# terraform_remote_state (see iac/modules/lambda-enrichment/main.tf).
module "lambda_enrichment" {
  source = "../modules/lambda-enrichment"

  prefix                 = local.prefix
  enrichment_role_arn    = module.iam_roles.enrichment_role_arn
  lambda_package_path    = var.enrichment_lambda_package_path
  idempotency_table_name = module.enrichment_idempotency_table.table_name
  failure_dlq_url        = module.review_queue.enrichment_failure_dlq_url
}

# Kafka event source mapping (self-managed broker, not MSK) wiring the
# Enrichment Lambda to consume Moderation's AssetMediaModerated topic.
module "kafka_event_source_mapping" {
  source = "../modules/kafka-event-source-mapping"

  prefix                  = local.prefix
  function_name           = module.lambda_enrichment.function_name
  kafka_bootstrap_servers = module.lambda_enrichment.kafka_bootstrap_servers
  subnet_ids              = module.lambda_enrichment.subnet_ids
  security_group_id       = module.lambda_enrichment.security_group_id
}
