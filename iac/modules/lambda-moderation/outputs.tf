output "function_name" {
  description = "Name of the moderation Lambda function"
  value       = aws_lambda_function.moderation.function_name
}

output "function_arn" {
  description = "ARN of the moderation Lambda function"
  value       = aws_lambda_function.moderation.arn
}

output "invoke_arn" {
  description = "Invoke ARN of the moderation Lambda function - consumed by iac/modules/s3-trigger to wire the S3 ObjectCreated notification + aws_lambda_permission (out of scope for this module)"
  value       = aws_lambda_function.moderation.invoke_arn
}

output "security_group_id" {
  description = "ID of the moderation Lambda's security group"
  value       = aws_security_group.moderation_lambda.id
}
