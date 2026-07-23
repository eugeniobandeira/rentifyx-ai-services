terraform {
  required_version = ">= 1.7"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }

  # Empty on purpose: values supplied via -backend-config flags at `terraform
  # init` time (bucket=rentifyx-tfstate-166613156216,
  # key=ai-services/terraform.tfstate, region=us-east-1,
  # dynamodb_table=rentifyx-tflock), not hardcoded here - same convention as
  # rentifyx-identity-api's iac/terraform/backend.tf. Terraform requires at
  # least an empty `backend "s3" {}` skeleton for CLI-flag partial
  # configuration to persist correctly between commands.
  backend "s3" {}
}
