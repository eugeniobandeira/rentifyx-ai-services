variable "bucket_name" {
  description = "Globally-unique S3 bucket name for asset media"
  type        = string
}

variable "versioning_enabled" {
  description = "Whether to enable S3 versioning on the media bucket"
  type        = bool
  default     = false
}
