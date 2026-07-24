# Generic single-partition-key DynamoDB table with optional TTL, PAY_PER_REQUEST billing.
# Reused by any Lambda's idempotency store (Moderation, Enrichment) - not feature-specific.

resource "aws_dynamodb_table" "this" {
  name         = var.table_name
  billing_mode = var.billing_mode
  hash_key     = var.hash_key

  attribute {
    name = var.hash_key
    type = var.hash_key_type
  }

  ttl {
    attribute_name = var.ttl_attribute_name
    enabled        = true
  }
}
