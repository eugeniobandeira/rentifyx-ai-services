# S3 -> Moderation + Dedupe Lambda trigger, via SNS fan-out.
#
# A direct aws_s3_bucket_notification with two lambda_function blocks that
# share the same prefix/suffix filter is REJECTED by S3 itself
# ("Configuration is ambiguously defined. Cannot have overlapping suffixes in
# two rules if the prefixes are overlapping for the same event type") -
# confirmed the hard way against real AWS 2026-07-27. S3's direct Lambda
# notification target only supports ONE destination per overlapping
# prefix/suffix combination; fanning the same event out to multiple Lambdas
# needs an intermediary. SNS was chosen over EventBridge specifically because
# SNS relays the exact same raw S3 event-notification JSON (as a string in
# each subscriber's Sns.Message) that a direct S3->Lambda notification would
# have delivered - ModerationHandler/DedupeHandler only had to swap their
# entrypoint type from S3Event to SNSEvent and deserialize that string back
# into the same S3Event shape, keeping ProcessAsync and every already-shipped
# unit/integration test untouched. EventBridge's own "Object Created" detail
# schema is a different shape entirely and would have required rewriting
# both handlers' parsing from scratch.
#
# filter_prefix defaults to "assets/" (the root module's variables.tf) - the
# assets/{ownerId}/{assetId}/{filename} key shape AssetKeyConventionFilter assumes,
# confirmed cross-repo against asset-registry-api's real S3MediaStorageService (G-001,
# closed) and already proven working in a real deploy. This module itself still takes
# it as a plain variable rather than hardcoding it, so a caller can override.

resource "aws_sns_topic" "asset_uploaded" {
  name = "${var.prefix}-asset-uploaded"
}

data "aws_iam_policy_document" "asset_uploaded_topic" {
  statement {
    sid    = "AllowS3Publish"
    effect = "Allow"

    principals {
      type        = "Service"
      identifiers = ["s3.amazonaws.com"]
    }

    actions   = ["sns:Publish"]
    resources = [aws_sns_topic.asset_uploaded.arn]

    condition {
      test     = "ArnEquals"
      variable = "aws:SourceArn"
      values   = [var.bucket_arn]
    }
  }
}

resource "aws_sns_topic_policy" "asset_uploaded" {
  arn    = aws_sns_topic.asset_uploaded.arn
  policy = data.aws_iam_policy_document.asset_uploaded_topic.json
}

resource "aws_s3_bucket_notification" "asset_upload_trigger" {
  bucket = var.bucket_id

  topic {
    topic_arn     = aws_sns_topic.asset_uploaded.arn
    events        = ["s3:ObjectCreated:*"]
    filter_prefix = var.filter_prefix
    filter_suffix = var.filter_suffix
  }

  depends_on = [aws_sns_topic_policy.asset_uploaded]
}

resource "aws_sns_topic_subscription" "moderation" {
  topic_arn = aws_sns_topic.asset_uploaded.arn
  protocol  = "lambda"
  endpoint  = var.lambda_function_arn
}

resource "aws_lambda_permission" "allow_sns_invoke_moderation" {
  statement_id  = "${var.prefix}-allow-sns-invoke-moderation"
  action        = "lambda:InvokeFunction"
  function_name = var.lambda_function_name
  principal     = "sns.amazonaws.com"
  source_arn    = aws_sns_topic.asset_uploaded.arn
}

resource "aws_sns_topic_subscription" "dedupe" {
  topic_arn = aws_sns_topic.asset_uploaded.arn
  protocol  = "lambda"
  endpoint  = var.dedupe_lambda_function_arn
}

resource "aws_lambda_permission" "allow_sns_invoke_dedupe" {
  statement_id  = "${var.prefix}-allow-sns-invoke-dedupe"
  action        = "lambda:InvokeFunction"
  function_name = var.dedupe_lambda_function_name
  principal     = "sns.amazonaws.com"
  source_arn    = aws_sns_topic.asset_uploaded.arn
}
