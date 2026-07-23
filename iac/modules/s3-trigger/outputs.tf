output "lambda_permission_id" {
  description = "ID of the aws_lambda_permission resource allowing S3 to invoke the moderation Lambda"
  value       = aws_lambda_permission.allow_s3_invoke_moderation.id
}

output "bucket_notification_id" {
  description = "ID of the aws_s3_bucket_notification resource wiring the S3 bucket to the moderation Lambda"
  value       = aws_s3_bucket_notification.moderation_trigger.id
}
