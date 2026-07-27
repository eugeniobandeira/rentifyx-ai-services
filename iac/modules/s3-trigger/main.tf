# S3 -> Moderation + Dedupe Lambda trigger. The bucket and the Lambdas are both owned
# by other modules (bucket: iac/modules/media-bucket, this repo's own root config;
# Lambdas: iac/modules/lambda-moderation, iac/modules/lambda-dedupe) — this module only
# wires the notification and the invoke permissions between them.
#
# A single S3 bucket supports only ONE aws_s3_bucket_notification resource (it's a full
# replace, not additive) - both Lambdas are wired as separate lambda_function blocks
# inside this one resource, not two separate resources.
#
# filter_prefix defaults to "assets/" (the root module's variables.tf) - the
# assets/{ownerId}/{assetId}/{filename} key shape AssetKeyConventionFilter assumes,
# confirmed cross-repo against asset-registry-api's real S3MediaStorageService (G-001,
# closed) and already proven working in a real deploy. This module itself still takes
# it as a plain variable rather than hardcoding it, so a caller can override.

resource "aws_lambda_permission" "allow_s3_invoke_moderation" {
  statement_id  = "${var.prefix}-allow-s3-invoke-moderation"
  action        = "lambda:InvokeFunction"
  function_name = var.lambda_function_name
  principal     = "s3.amazonaws.com"
  source_arn    = var.bucket_arn
}

resource "aws_lambda_permission" "allow_s3_invoke_dedupe" {
  statement_id  = "${var.prefix}-allow-s3-invoke-dedupe"
  action        = "lambda:InvokeFunction"
  function_name = var.dedupe_lambda_function_name
  principal     = "s3.amazonaws.com"
  source_arn    = var.bucket_arn
}

resource "aws_s3_bucket_notification" "asset_upload_trigger" {
  bucket = var.bucket_id

  lambda_function {
    lambda_function_arn = var.lambda_function_arn
    events              = ["s3:ObjectCreated:*"]
    filter_prefix       = var.filter_prefix
    filter_suffix       = var.filter_suffix
  }

  lambda_function {
    lambda_function_arn = var.dedupe_lambda_function_arn
    events              = ["s3:ObjectCreated:*"]
    filter_prefix       = var.filter_prefix
    filter_suffix       = var.filter_suffix
  }

  depends_on = [
    aws_lambda_permission.allow_s3_invoke_moderation,
    aws_lambda_permission.allow_s3_invoke_dedupe,
  ]
}
