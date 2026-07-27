# S3 -> Moderation Lambda trigger. The bucket and the Lambda are both owned by other
# modules (bucket: iac/modules/media-bucket, this repo's own root config; Lambda:
# iac/modules/lambda-moderation, built in parallel) — this module only wires the
# notification and the invoke permission between them.
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

resource "aws_s3_bucket_notification" "moderation_trigger" {
  bucket = var.bucket_id

  lambda_function {
    lambda_function_arn = var.lambda_function_arn
    events              = ["s3:ObjectCreated:*"]
    filter_prefix       = var.filter_prefix
    filter_suffix       = var.filter_suffix
  }

  depends_on = [aws_lambda_permission.allow_s3_invoke_moderation]
}
