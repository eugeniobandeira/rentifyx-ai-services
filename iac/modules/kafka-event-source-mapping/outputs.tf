output "event_source_mapping_uuid" {
  description = "UUID of the Kafka event source mapping, useful for referencing/debugging via the AWS CLI"
  value       = aws_lambda_event_source_mapping.enrichment.uuid
}
