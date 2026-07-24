output "review_queue_arn" {
  description = "ARN of the manual review SQS queue"
  value       = aws_sqs_queue.review.arn
}

output "review_queue_url" {
  description = "URL of the manual review SQS queue"
  value       = aws_sqs_queue.review.url
}

output "review_dlq_arn" {
  description = "ARN of the manual review DLQ"
  value       = aws_sqs_queue.review_dlq.arn
}

output "moderation_failure_dlq_arn" {
  description = "ARN of the DLQ for Rekognition invocations that fail after retries are exhausted"
  value       = aws_sqs_queue.moderation_failure_dlq.arn
}

output "moderation_failure_dlq_url" {
  description = "URL of the DLQ for Rekognition invocations that fail after retries are exhausted (needed by iac/modules/lambda-moderation to inject FAILURE_DLQ_URL - sqs:SendMessage needs a queue URL, not just an ARN)"
  value       = aws_sqs_queue.moderation_failure_dlq.url
}

output "enrichment_failure_dlq_arn" {
  description = "ARN of the DLQ for Enrichment failures (Bedrock invocation or Kafka publish failures after retries are exhausted)"
  value       = aws_sqs_queue.enrichment_failure_dlq.arn
}

output "enrichment_failure_dlq_url" {
  description = "URL of the DLQ for Enrichment failures (needed by iac/modules/lambda-enrichment to inject its failure DLQ URL - sqs:SendMessage needs a queue URL, not just an ARN)"
  value       = aws_sqs_queue.enrichment_failure_dlq.url
}
