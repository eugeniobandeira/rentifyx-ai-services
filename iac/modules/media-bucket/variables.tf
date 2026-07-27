variable "bucket_name" {
  description = "Globally-unique S3 bucket name for asset media"
  type        = string
}

variable "versioning_enabled" {
  description = "Whether to enable S3 versioning on the media bucket"
  type        = bool
  default     = false
}

# TEMPORARY (2026-07-27): defaults to "*" until rentifyx-frontend has a fixed
# real origin (it's never been deployed anywhere - no S3/CloudFront/Amplify
# host exists yet, only ad-hoc local dev / EC2 testing). Same posture
# rentifyx-identity-api's CorsExtension.cs already takes for its own CORS
# policy (SetIsOriginAllowed(_ => true)) for the same reason. Narrow this to
# the real origin(s) once the frontend has one.
variable "cors_allowed_origins" {
  description = "Origins allowed to PUT directly to this bucket via a presigned URL (rentifyx-frontend's direct-to-S3 upload flow)"
  type        = list(string)
  default     = ["*"]
}
