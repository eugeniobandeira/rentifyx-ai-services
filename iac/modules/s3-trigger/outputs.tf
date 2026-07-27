output "topic_arn" {
  description = "ARN of the SNS topic S3 publishes ObjectCreated events to, fanned out to both the moderation and dedupe Lambdas"
  value       = aws_sns_topic.asset_uploaded.arn
}

output "moderation_lambda_permission_id" {
  description = "ID of the aws_lambda_permission resource allowing SNS to invoke the moderation Lambda"
  value       = aws_lambda_permission.allow_sns_invoke_moderation.id
}

output "dedupe_lambda_permission_id" {
  description = "ID of the aws_lambda_permission resource allowing SNS to invoke the dedupe Lambda"
  value       = aws_lambda_permission.allow_sns_invoke_dedupe.id
}

output "bucket_notification_id" {
  description = "ID of the aws_s3_bucket_notification resource wiring the S3 bucket to the SNS topic"
  value       = aws_s3_bucket_notification.asset_upload_trigger.id
}
