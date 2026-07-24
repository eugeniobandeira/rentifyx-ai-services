variable "table_name" {
  description = "Name of the DynamoDB table"
  type        = string
}

variable "hash_key" {
  description = "Name of the table's partition (hash) key attribute"
  type        = string
  default     = "IdempotencyKey"
}

variable "hash_key_type" {
  description = "DynamoDB attribute type of the hash key (S, N, or B)"
  type        = string
  default     = "S"
}

variable "ttl_attribute_name" {
  description = "Name of the attribute used for the table's TTL expiry"
  type        = string
  default     = "ExpiresAt"
}

variable "billing_mode" {
  description = "DynamoDB billing mode"
  type        = string
  default     = "PAY_PER_REQUEST"
}
