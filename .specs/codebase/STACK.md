# Stack

- .NET 10
- AWS Lambda managed runtime
- Native AOT evaluation per function (open optimization, not default — ADR-AI-001)
- AWSSDK.Rekognition
- AWSSDK.BedrockRuntime
- AWSSDK.DynamoDBv2
- AWSSDK.SQS
- AWSSDK.S3
- Confluent.Kafka
- Amazon.Lambda.Core / Amazon.Lambda.S3Events / Amazon.Lambda.Serialization.SystemTextJson
- Terraform
- GitHub Actions
- Amazon.Lambda.Tools
- OpenTelemetry

## Test stack

- xunit, Moq, FluentAssertions
- Testcontainers.LocalStack, Testcontainers.Kafka (integration tests — require a running Docker daemon)

## Package version discipline

Central package management via `Directory.Packages.props`. Several original pins (from E-01 scaffolding) had already been pulled from NuGet.org by the time E-02 was implemented — always verify against `https://api.nuget.org/v3-flatcontainer/<id>/index.json` before pinning a version; never guess.
