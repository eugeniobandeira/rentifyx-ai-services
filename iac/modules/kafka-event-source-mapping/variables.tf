variable "prefix" {
  description = "Naming prefix, used to derive the default consumer group id"
  type        = string
}

variable "function_name" {
  description = "Name of the enrichment Lambda function to attach this event source mapping to (from lambda-enrichment's function_name output)"
  type        = string
}

variable "topics" {
  description = "Kafka topics to consume. Must match lambda-moderation's kafka_moderated_topic variable's default value (asset-media-moderated) - this is the topic Moderation publishes AssetMediaModerated events to"
  type        = list(string)
  default     = ["asset-media-moderated"]
}

variable "starting_position" {
  description = "Position in the topic to start reading from when a consumer group has no committed offset"
  type        = string
  default     = "TRIM_HORIZON"
}

variable "kafka_bootstrap_servers" {
  description = "Kafka bootstrap servers endpoint (host:port), passed through from lambda-enrichment's resolved value - not re-queried from terraform_remote_state here"
  type        = string
}

variable "subnet_ids" {
  description = "Subnet IDs the enrichment Lambda is VPC-attached to (from lambda-enrichment's subnet_ids output)"
  type        = list(string)
}

variable "security_group_id" {
  description = "Security group ID of the enrichment Lambda (from lambda-enrichment's security_group_id output)"
  type        = string
}

variable "consumer_group_id" {
  description = "Kafka consumer group id for this event source mapping"
  type        = string
  default     = null
}
