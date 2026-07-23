# S3 -> Moderation Lambda trigger. The bucket and the Lambda are both owned by other
# modules (bucket: likely rentifyx-platform or a root config not yet written; Lambda:
# iac/modules/lambda-moderation, built in parallel) — this module only wires the
# notification and the invoke permission between them.
#
# filter_prefix/filter_suffix intentionally have no baked-in convention: the
# assets/{ownerId}/{assetId}/{filename} key shape AssetKeyConventionFilter assumes is
# still unconfirmed with asset-registry-api (G-001). The root module must supply the
# real values once confirmed.

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
