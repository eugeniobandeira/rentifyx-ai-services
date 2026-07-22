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
