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

  statement {
    sid    = "KafkaPublishVerdict"
    effect = "Allow"

    # MSK IAM auth actions do not support wildcard resources beyond cluster/topic ARNs.
    actions = [
      "kafka-cluster:Connect",
      "kafka-cluster:DescribeTopic",
      "kafka-cluster:WriteData",
    ]

    resources = [
      var.moderation_kafka_cluster_arn,
      var.moderation_kafka_topic_arn,
    ]
  }

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

# --- Enrichment ---------------------------------------------------------

data "aws_iam_policy_document" "enrichment" {
  statement {
    sid    = "BedrockInvoke"
    effect = "Allow"

    actions = ["bedrock:InvokeModel"]

    resources = [var.bedrock_model_arn]
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
