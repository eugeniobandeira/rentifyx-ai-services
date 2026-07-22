variable "prefix" {
  description = "Resource name prefix"
  type        = string
}

variable "max_receive_count" {
  description = "Number of receives before a message moves to the DLQ"
  type        = number
  default     = 5
}

variable "alarm_queue_depth_threshold" {
  description = "ApproximateNumberOfMessagesVisible threshold that triggers the review-queue depth alarm"
  type        = number
  default     = 100
}

variable "alarm_evaluation_periods" {
  description = "Number of consecutive 5-minute periods the threshold must be breached before the alarm fires (12 periods = 1 hour)"
  type        = number
  default     = 12
}

variable "alarm_actions" {
  description = "ARNs to notify (e.g. an SNS topic) when the review-queue depth alarm fires"
  type        = list(string)
  default     = []
}
