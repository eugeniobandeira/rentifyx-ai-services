# Moderation Lambda function - consumes S3 ObjectCreated events, calls
# Rekognition DetectModerationLabels, and publishes moderation verdicts to
# Kafka / the manual review SQS queue (E-02, ADR-AI-003/004).
#
# The S3 bucket notification + aws_lambda_permission allowing S3 to invoke
# this function are NOT defined here - that's iac/modules/s3-trigger's job.
# This module only exposes the function's ARN/invoke ARN (see outputs.tf)
# for that module to wire against.

# --- Cross-repo: rentifyx-platform's shared VPC + self-hosted Kafka broker ---
#
# Same pattern as rentifyx-identity-api's iac/terraform/main.tf: read the
# platform repo's state read-only via terraform_remote_state (same account/
# bucket this repo's own backend uses), resolve the Kafka bootstrap address
# once via SSM at deploy time, and VPC-attach this Lambda so it can reach the
# broker (PLAINTEXT, port 9092, self-hosted EC2/KRaft - not MSK, no runtime
# Kafka IAM permission needed, see iac/modules/iam-roles/main.tf's comment).
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
# before that (same reasoning as rentifyx-identity-api's main.tf). Without a
# real value here, KafkaProducerFactory throws at cold start (see the
# 2026-07-20 OutboxPublisher crash-loop noted in that repo, caused by exactly
# this env var being missing).
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
resource "aws_security_group" "moderation_lambda" {
  name        = "${var.prefix}-moderation-lambda-sg"
  description = "Egress-only SG for the moderation Lambda - VPC-attached to reach the rentifyx-platform self-hosted Kafka broker"
  vpc_id      = data.terraform_remote_state.platform.outputs.vpc_id

  egress {
    description = "All outbound - Kafka broker (port 9092) plus AWS API endpoints (Rekognition, DynamoDB, SQS)"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
}

resource "aws_lambda_function" "moderation" {
  function_name = "${var.prefix}-moderation"
  description   = "Consumes S3 ObjectCreated events, calls Rekognition DetectModerationLabels, publishes moderation verdicts (E-02, ADR-AI-003/004)"

  role    = var.moderation_role_arn # ADR-AI-002: moderation-only role, never shared
  handler = var.lambda_handler
  runtime = var.lambda_runtime # managed .NET runtime zip, not Native AOT (ADR-AI-001)

  filename         = var.lambda_package_path
  source_code_hash = filebase64sha256(var.lambda_package_path)

  timeout     = var.timeout
  memory_size = var.memory_size

  # Private subnet, not public - confirmed the hard way against real AWS
  # 2026-07-24: a Lambda ENI never gets a public IP even in a subnet with an
  # IGW route, so public_subnets left this function with zero egress to any
  # AWS public API (Rekognition, DynamoDB, SQS all timed out). rentifyx-
  # platform now exposes private_subnets (NAT egress) for exactly this.
  vpc_config {
    subnet_ids         = [data.terraform_remote_state.platform.outputs.private_subnets[0]]
    security_group_ids = [aws_security_group.moderation_lambda.id]
  }

  environment {
    variables = {
      IDEMPOTENCY_TABLE_NAME     = var.idempotency_table_name
      KAFKA_BOOTSTRAP_SERVERS    = try(data.aws_ssm_parameter.kafka_bootstrap_servers[0].value, "")
      KAFKA_MODERATED_TOPIC      = var.kafka_moderated_topic
      KAFKA_PENDING_REVIEW_TOPIC = var.kafka_pending_review_topic
      REVIEW_QUEUE_URL           = var.review_queue_url
      FAILURE_DLQ_URL            = var.failure_dlq_url
    }
  }
}
