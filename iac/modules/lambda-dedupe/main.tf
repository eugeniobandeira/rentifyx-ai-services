# Dedupe Lambda function - consumes S3 ObjectCreated events, computes a
# perceptual hash (average hash / aHash) of the uploaded image, and publishes
# AssetDuplicateDetected to Kafka when the hash matches a different asset
# (DEF-AI-001, ADR-AI-007).
#
# The S3 bucket notification + aws_lambda_permission allowing S3 to invoke
# this function are NOT defined here - that's iac/modules/s3-trigger's job.
# This module only exposes the function's ARN/invoke ARN (see outputs.tf)
# for that module to wire against. Same split of responsibility as
# iac/modules/lambda-moderation.

# --- Cross-repo: rentifyx-platform's shared VPC + self-hosted Kafka broker ---
#
# Dedupe publishes AssetDuplicateDetected to the same self-hosted Kafka broker
# Moderation/Enrichment use - same terraform_remote_state + SSM + VPC-attach
# pattern as iac/modules/lambda-moderation (see that module's main.tf for the
# full reasoning).
data "terraform_remote_state" "platform" {
  backend = "s3"

  config = {
    bucket = "rentifyx-tfstate-166613156216"
    key    = "platform/terraform.tfstate"
    region = "us-east-1"
  }
}

locals {
  kafka_ssm_parameter_path = try(data.terraform_remote_state.platform.outputs.kafka_ssm_parameter_path, "")
}

data "aws_ssm_parameter" "kafka_bootstrap_servers" {
  count           = local.kafka_ssm_parameter_path != "" ? 1 : 0
  name            = local.kafka_ssm_parameter_path
  with_decryption = true
}

# Lambda's own security group, egress-only - same reasoning as
# iac/modules/lambda-moderation's security group (Kafka broker's own SG
# allows ingress from anywhere inside vpc_cidr, no ingress rule needed here).
resource "aws_security_group" "dedupe_lambda" {
  name        = "${var.prefix}-dedupe-lambda-sg"
  description = "Egress-only SG for the dedupe Lambda - VPC-attached to reach the rentifyx-platform self-hosted Kafka broker"
  vpc_id      = data.terraform_remote_state.platform.outputs.vpc_id

  egress {
    description = "All outbound - Kafka broker (port 9092) plus AWS API endpoints (S3, DynamoDB, SQS)"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
}

resource "aws_lambda_function" "dedupe" {
  function_name = "${var.prefix}-dedupe"
  description   = "Consumes S3 ObjectCreated events, computes a perceptual image hash, publishes AssetDuplicateDetected on a hash match (DEF-AI-001, ADR-AI-007)"

  role    = var.dedupe_role_arn # ADR-AI-002: dedupe-only role, never shared
  handler = var.lambda_handler
  runtime = var.lambda_runtime # managed .NET runtime zip, not Native AOT (ADR-AI-001)

  filename         = var.lambda_package_path
  source_code_hash = filebase64sha256(var.lambda_package_path)

  timeout     = var.timeout
  memory_size = var.memory_size

  # Private subnet, not public - same real bug this guards against as
  # iac/modules/lambda-moderation (a Lambda ENI never gets a public IP even
  # with an IGW route; private_subnets have NAT egress instead).
  vpc_config {
    subnet_ids         = [data.terraform_remote_state.platform.outputs.private_subnets[0]]
    security_group_ids = [aws_security_group.dedupe_lambda.id]
  }

  environment {
    variables = {
      IDEMPOTENCY_TABLE_NAME       = var.idempotency_table_name
      HASH_TABLE_NAME              = var.hash_table_name
      KAFKA_BOOTSTRAP_SERVERS      = try(data.aws_ssm_parameter.kafka_bootstrap_servers[0].value, "")
      KAFKA_DUPLICATE_DETECTED_TOPIC = var.kafka_duplicate_detected_topic
      FAILURE_DLQ_URL              = var.failure_dlq_url
    }
  }
}
