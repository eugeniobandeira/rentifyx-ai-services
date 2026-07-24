# One IAM role per Lambda — no shared execution role (ADR-AI-002).

data "aws_iam_policy_document" "lambda_assume_role" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

# --- Moderation -------------------------------------------------------

data "aws_iam_policy_document" "moderation" {
  statement {
    sid    = "RekognitionModeration"
    effect = "Allow"

    actions = ["rekognition:DetectModerationLabels"]

    resources = ["*"] # DetectModerationLabels does not support resource-level scoping
  }

  statement {
    sid    = "MediaBucketRead"
    effect = "Allow"

    actions = ["s3:GetObject"]

    resources = ["${var.media_bucket_arn}/*"]
  }

  statement {
    sid    = "IdempotencyTableWrite"
    effect = "Allow"

    actions = ["dynamodb:PutItem"]

    resources = [var.moderation_idempotency_table_arn]
  }

  # No Kafka IAM statement here: `rentifyx-platform`'s module.kafka is a self-hosted
  # KRaft broker on EC2 (PLAINTEXT, port 9092) — MSK Serverless/IAM auth was evaluated
  # and replaced (rentifyx-platform ADR-002/self-hosted-kafka), so `kafka-cluster:*`
  # actions don't apply to this broker at all. Reachability is VPC/security-group based
  # (rentifyx-platform's kafka SG allows any client inside `vpc_cidr`), which means this
  # Lambda must be VPC-attached — a `lambda-moderation` Terraform concern (not yet built),
  # not an IAM policy statement. The bootstrap address itself is read once via
  # `terraform_remote_state` + `aws_ssm_parameter` at deploy time and injected as a Lambda
  # environment variable, same pattern `rentifyx-identity-api`'s EC2 module already uses
  # (`iac/terraform/main.tf` there) — no runtime `ssm:GetParameter` permission needed either.

  statement {
    sid    = "ReviewQueueAndDlqSend"
    effect = "Allow"

    actions = ["sqs:SendMessage"]

    resources = [
      var.moderation_review_queue_arn,
      var.moderation_failure_dlq_arn,
    ]
  }
}

resource "aws_iam_role" "moderation" {
  name               = "${var.prefix}-moderation-role"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume_role.json
}

resource "aws_iam_role_policy" "moderation" {
  name   = "${var.prefix}-moderation-policy"
  role   = aws_iam_role.moderation.id
  policy = data.aws_iam_policy_document.moderation.json
}

# Both moderation and enrichment are VPC-attached (to reach the
# rentifyx-platform self-hosted Kafka broker) - Lambda VPC attachment always
# requires ec2:CreateNetworkInterface/DeleteNetworkInterface/
# DescribeNetworkInterfaces on the execution role, confirmed the hard way
# against real AWS 2026-07-24 (CreateFunction failed with
# "does not have permissions to call CreateNetworkInterface on EC2" without
# this). AWS's own managed policy is the standard way to grant it.
resource "aws_iam_role_policy_attachment" "moderation_vpc_access" {
  role       = aws_iam_role.moderation.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaVPCAccessExecutionRole"
}

# --- Enrichment ---------------------------------------------------------

data "aws_iam_policy_document" "enrichment" {
  statement {
    sid    = "BedrockInvoke"
    effect = "Allow"

    actions = ["bedrock:InvokeModel"]

    resources = [var.bedrock_model_arn]
  }

  statement {
    sid    = "S3Read"
    effect = "Allow"

    actions = ["s3:GetObject"]

    resources = ["${var.media_bucket_arn}/*"]
  }

  statement {
    sid    = "IdempotencyTableWrite"
    effect = "Allow"

    actions = ["dynamodb:PutItem"]

    resources = [var.enrichment_idempotency_table_arn]
  }

  statement {
    sid    = "FailureDlqSend"
    effect = "Allow"

    actions = ["sqs:SendMessage"]

    resources = [var.enrichment_failure_dlq_arn]
  }

  # Self-managed Kafka event source mappings need these Describe permissions
  # on top of AWSLambdaVPCAccessExecutionRole's CreateNetworkInterface set -
  # confirmed the hard way against real AWS 2026-07-24
  # (CreateEventSourceMapping failed with "Cannot access security groups...
  # ensure the role can perform the 'ec2:DescribeSecurityGroups' action").
  # No resource-level scoping support for these Describe actions.
  statement {
    sid    = "KafkaEventSourceMappingVpcDescribe"
    effect = "Allow"

    actions = [
      "ec2:DescribeSecurityGroups",
      "ec2:DescribeSubnets",
      "ec2:DescribeVpcs",
    ]

    resources = ["*"]
  }
}

resource "aws_iam_role" "enrichment" {
  name               = "${var.prefix}-enrichment-role"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume_role.json
}

resource "aws_iam_role_policy" "enrichment" {
  name   = "${var.prefix}-enrichment-policy"
  role   = aws_iam_role.enrichment.id
  policy = data.aws_iam_policy_document.enrichment.json
}

resource "aws_iam_role_policy_attachment" "enrichment_vpc_access" {
  role       = aws_iam_role.enrichment.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaVPCAccessExecutionRole"
}

# --- Dedupe (DEF-AI-001, role scaffolded ahead of implementation) -------

data "aws_iam_policy_document" "dedupe" {
  statement {
    sid    = "RekognitionCompareFaces"
    effect = "Allow"

    # Placeholder action set — the dedupe Lambda itself is deferred (DEF-AI-001).
    # Scoped narrowly now so the role isn't a blank check when implementation lands.
    actions = ["rekognition:CompareFaces"]

    resources = ["*"] # CompareFaces does not support resource-level scoping
  }
}

resource "aws_iam_role" "dedupe" {
  name               = "${var.prefix}-dedupe-role"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume_role.json
}

resource "aws_iam_role_policy" "dedupe" {
  name   = "${var.prefix}-dedupe-policy"
  role   = aws_iam_role.dedupe.id
  policy = data.aws_iam_policy_document.dedupe.json
}
