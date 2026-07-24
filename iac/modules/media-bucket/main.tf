# Asset media bucket - domain-specific to this repo (Moderation reads it via
# S3 ObjectCreated events, Enrichment reads the same object off the
# AssetMediaModerated event's Bucket/Key). Owned here rather than
# rentifyx-platform: this bucket belongs to the AI-services domain, not
# shared cross-domain infra like the VPC/Kafka broker platform owns.
resource "aws_s3_bucket" "media" {
  bucket = var.bucket_name
}

# Private by default - Rekognition/Bedrock read via IAM-scoped GetObject
# (iac/modules/iam-roles), not public URLs.
resource "aws_s3_bucket_public_access_block" "media" {
  bucket = aws_s3_bucket.media.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_versioning" "media" {
  bucket = aws_s3_bucket.media.id

  versioning_configuration {
    status = var.versioning_enabled ? "Enabled" : "Disabled"
  }
}
