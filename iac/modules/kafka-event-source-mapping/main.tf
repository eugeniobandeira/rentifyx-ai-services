# Kafka event source mapping wiring the Enrichment Lambda to consume the
# AssetMediaModerated topic from rentifyx-platform's self-hosted Kafka
# broker (PLAINTEXT, self-managed EC2/KRaft - NOT Amazon MSK, so this uses
# self_managed_event_source / self_managed_kafka_event_source_config, not
# the amazon_managed_kafka_event_source_config shape).
#
# This is the first self-managed Kafka event source mapping in this repo -
# no existing pattern to copy from. Shape confirmed against the AWS
# provider's own docs (verified via Context7 during design) - do not
# second-guess or alter without re-confirming against the provider docs.
#
# var.consumer_group_id defaults to null (Terraform variable defaults can't
# reference other variables), resolved here via coalesce() to
# "${var.prefix}-enrichment-consumer" when unset.
locals {
  consumer_group_id = coalesce(var.consumer_group_id, "${var.prefix}-enrichment-consumer")
}

resource "aws_lambda_event_source_mapping" "enrichment" {
  function_name     = var.function_name
  topics            = var.topics
  starting_position = var.starting_position

  self_managed_event_source {
    endpoints = {
      KAFKA_BOOTSTRAP_SERVERS = var.kafka_bootstrap_servers
    }
  }

  self_managed_kafka_event_source_config {
    consumer_group_id = local.consumer_group_id
  }

  # One VPC_SUBNET entry per var.subnet_ids element - dynamic block since
  # lambda-enrichment's subnet_ids output is a list (currently always a
  # single element in practice, but this doesn't assume that).
  dynamic "source_access_configuration" {
    for_each = var.subnet_ids
    content {
      type = "VPC_SUBNET"
      uri  = "subnet:${source_access_configuration.value}"
    }
  }

  # Broker is PLAINTEXT - no SASL/auth-type source_access_configuration
  # entry applies here, only VPC networking config.
  source_access_configuration {
    type = "VPC_SECURITY_GROUP"
    uri  = "security_group:${var.security_group_id}"
  }
}
