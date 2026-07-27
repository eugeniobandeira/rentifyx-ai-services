output "function_name" {
  description = "Name of the dedupe Lambda function"
  value       = aws_lambda_function.dedupe.function_name
}

output "function_arn" {
  description = "ARN of the dedupe Lambda function"
  value       = aws_lambda_function.dedupe.arn
}

output "invoke_arn" {
  description = "Invoke ARN of the dedupe Lambda function - consumed by iac/modules/s3-trigger to wire the S3 ObjectCreated notification + aws_lambda_permission (out of scope for this module)"
  value       = aws_lambda_function.dedupe.invoke_arn
}

output "security_group_id" {
  description = "ID of the dedupe Lambda's security group"
  value       = aws_security_group.dedupe_lambda.id
}
