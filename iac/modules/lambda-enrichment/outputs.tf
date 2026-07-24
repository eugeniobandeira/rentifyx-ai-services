output "function_name" {
  description = "Name of the enrichment Lambda function"
  value       = aws_lambda_function.enrichment.function_name
}

output "function_arn" {
  description = "ARN of the enrichment Lambda function"
  value       = aws_lambda_function.enrichment.arn
}

output "security_group_id" {
  description = "ID of the enrichment Lambda's security group"
  value       = aws_security_group.enrichment_lambda.id
}

output "subnet_ids" {
  description = "Subnet IDs this Lambda is VPC-attached to, so a downstream Kafka event-source-mapping module doesn't need to re-derive VPC placement"
  value       = [data.terraform_remote_state.platform.outputs.private_subnets[0]]
}

output "kafka_bootstrap_servers" {
  description = "Resolved Kafka bootstrap address (same try()-wrapped SSM lookup used for this Lambda's own KAFKA_BOOTSTRAP_SERVERS env var), so a downstream Kafka event-source-mapping module doesn't independently re-query rentifyx-platform's remote state"
  value       = try(data.aws_ssm_parameter.kafka_bootstrap_servers[0].value, "")
}
