# Enrichment Lambda function - Kafka-triggered (AWS Lambda Kafka event
# source mapping for self-managed Kafka), consumes AssetMediaModerated v2
# events, fetches the asset from S3, calls Bedrock Converse (Claude Sonnet
# 5) for structured enrichment suggestions, and publishes
# AssetEnrichmentSuggested to Kafka / a failure DLQ on error (E-03,
# ADR-AI-005/006).
#
# The Kafka event source mapping (aws_lambda_event_source_mapping) is NOT
# defined here - that's iac/modules/kafka-event-source-mapping's job. This
# module only exposes the function's ARN/name (see outputs.tf) for that
# module to wire against.

# --- Cross-repo: rentifyx-platform's shared VPC + self-hosted Kafka broker ---
#
# Same pattern as rentifyx-identity-api's iac/terraform/main.tf and this
# repo's iac/modules/lambda-moderation: read the platform repo's state
# read-only via terraform_remote_state (same account/bucket this repo's own
# backend uses), resolve the Kafka bootstrap address once via SSM at deploy
# time, and VPC-attach this Lambda so it can reach the broker (PLAINTEXT,
# port 9092, self-hosted EC2/KRaft - not MSK, no runtime Kafka IAM
# permission needed, see iac/modules/iam-roles/main.tf's comment).
data "terraform_remote_state" "platform" {
  backend = "s3"

  config = {
    bucket = "rentifyx-tfstate-166613156216"
    key    = "platform/terraform.tfstate"
    region = "us-east-1"
  }
}

# The SSM parameter only exists once rentifyx-platform's module.kafka has
# been applied - try() so this module's own plan/apply doesn't hard-fail
# before that (same reasoning as rentifyx-identity-api's main.tf and
# lambda-moderation). Without a real value here, the Kafka producer throws
# at cold start.
locals {
  kafka_ssm_parameter_path = try(data.terraform_remote_state.platform.outputs.kafka_ssm_parameter_path, "")
}

data "aws_ssm_parameter" "kafka_bootstrap_servers" {
  count           = local.kafka_ssm_parameter_path != "" ? 1 : 0
  name            = local.kafka_ssm_parameter_path
  with_decryption = true
}

# Lambda's own security group, egress-only. rentifyx-platform's Kafka broker
# SG allows ingress from any client inside vpc_cidr (see
# iac/modules/iam-roles/main.tf's comment), so no ingress rule or cross-SG
# reference back to the broker's SG is needed here.
resource "aws_security_group" "enrichment_lambda" {
  name        = "${var.prefix}-enrichment-lambda-sg"
  description = "Egress-only SG for the enrichment Lambda - VPC-attached to reach rentifyx-platform's self-hosted Kafka broker"
  vpc_id      = data.terraform_remote_state.platform.outputs.vpc_id

  egress {
    description = "All outbound - Kafka broker (port 9092) plus AWS API endpoints (Bedrock, S3, DynamoDB, SQS)"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
}

resource "aws_lambda_function" "enrichment" {
  function_name = "${var.prefix}-enrichment"
  description   = "Kafka-triggered: consumes AssetMediaModerated events, calls Bedrock Converse for enrichment suggestions, publishes AssetEnrichmentSuggested (E-03, ADR-AI-005/006)"

  role    = var.enrichment_role_arn # ADR-AI-002: enrichment-only role, never shared
  handler = var.lambda_handler
  runtime = var.lambda_runtime # managed .NET runtime zip, not Native AOT (ADR-AI-001)

  filename         = var.lambda_package_path
  source_code_hash = filebase64sha256(var.lambda_package_path)

  timeout     = var.timeout
  memory_size = var.memory_size

  # rentifyx-platform's root outputs only expose public_subnets (not
  # private_subnets) today, so this mirrors rentifyx-identity-api's EC2
  # module and lambda-moderation exactly (public_subnets[0]) rather than
  # inventing a private-subnet path platform doesn't currently output.
  vpc_config {
    subnet_ids         = [data.terraform_remote_state.platform.outputs.public_subnets[0]]
    security_group_ids = [aws_security_group.enrichment_lambda.id]
  }

  environment {
    variables = {
      ENRICHMENT_IDEMPOTENCY_TABLE_NAME = var.idempotency_table_name
      ENRICHMENT_FAILURE_DLQ_URL        = var.failure_dlq_url
      BEDROCK_REGION                    = var.bedrock_region
      KAFKA_ENRICHMENT_SUGGESTED_TOPIC  = var.kafka_enrichment_suggested_topic
      KAFKA_BOOTSTRAP_SERVERS           = try(data.aws_ssm_parameter.kafka_bootstrap_servers[0].value, "")
    }
  }
}
