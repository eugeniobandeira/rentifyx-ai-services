output "moderation_role_arn" {
  description = "ARN of the moderation Lambda's IAM role"
  value       = aws_iam_role.moderation.arn
}

output "enrichment_role_arn" {
  description = "ARN of the enrichment Lambda's IAM role"
  value       = aws_iam_role.enrichment.arn
}

output "dedupe_role_arn" {
  description = "ARN of the dedupe Lambda's IAM role (scaffolded ahead of DEF-AI-001)"
  value       = aws_iam_role.dedupe.arn
}
