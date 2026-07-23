output "moderation_role_arn" {
  description = "ARN of the moderation Lambda's IAM role"
  value       = module.iam_roles.moderation_role_arn
}

output "enrichment_role_arn" {
  description = "ARN of the enrichment Lambda's IAM role (role exists ahead of E-03/E-04 implementation)"
  value       = module.iam_roles.enrichment_role_arn
}

output "dedupe_role_arn" {
  description = "ARN of the dedupe Lambda's IAM role (scaffolded ahead of DEF-AI-001)"
  value       = module.iam_roles.dedupe_role_arn
}

output "review_queue_url" {
  description = "URL of the manual review SQS queue"
  value       = module.review_queue.review_queue_url
}

output "review_dlq_arn" {
  description = "ARN of the manual review DLQ"
  value       = module.review_queue.review_dlq_arn
}

output "moderation_failure_dlq_url" {
  description = "URL of the DLQ for Rekognition invocations that fail after retries are exhausted"
  value       = module.review_queue.moderation_failure_dlq_url
}

output "moderation_function_arn" {
  description = "ARN of the moderation Lambda function"
  value       = module.lambda_moderation.function_arn
}

output "moderation_function_name" {
  description = "Name of the moderation Lambda function"
  value       = module.lambda_moderation.function_name
}

output "moderation_security_group_id" {
  description = "ID of the moderation Lambda's security group"
  value       = module.lambda_moderation.security_group_id
}
