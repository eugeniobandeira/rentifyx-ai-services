terraform {
  required_version = ">= 1.7"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      # Bumped from ~> 5.0 (2026-07-24): v5.100.0's schema predates AWS
      # Lambda's dotnet10 managed runtime (added by AWS 2026-01-08,
      # confirmed via aws.amazon.com/about-aws/whats-new/2026/01/aws-lambda-dot-net-10)
      # and rejected it client-side as an invalid enum value. All child
      # modules under iac/modules/ already resolve unconstrained to 6.56.0.
      version = "~> 6.0"
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
