# Manual review queue + DLQ for moderation verdicts of PendingReview (MOD-04),
# plus a separate DLQ for Rekognition invocations that fail after retries are exhausted (MOD-03 AC3).

resource "aws_sqs_queue" "review_dlq" {
  name = "${var.prefix}-moderation-review-dlq"
}

resource "aws_sqs_queue" "moderation_failure_dlq" {
  name = "${var.prefix}-moderation-failure-dlq"
}

resource "aws_sqs_queue" "enrichment_failure_dlq" {
  name = "${var.prefix}-enrichment-failure-dlq"
}

resource "aws_sqs_queue" "review" {
  name = "${var.prefix}-moderation-review-queue"

  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.review_dlq.arn
    maxReceiveCount     = var.max_receive_count
  })
}

# Alarms when the review queue backs up for over an hour (spec's Edge Cases, MOD-04 SLA risk).
resource "aws_cloudwatch_metric_alarm" "review_queue_depth" {
  alarm_name          = "${var.prefix}-moderation-review-queue-depth"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = var.alarm_evaluation_periods
  metric_name         = "ApproximateNumberOfMessagesVisible"
  namespace           = "AWS/SQS"
  period              = 300
  statistic           = "Average"
  threshold           = var.alarm_queue_depth_threshold
  alarm_description   = "Moderation review queue depth exceeded threshold for over an hour"
  alarm_actions       = var.alarm_actions

  dimensions = {
    QueueName = aws_sqs_queue.review.name
  }
}
