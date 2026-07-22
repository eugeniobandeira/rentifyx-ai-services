# State

## Decisions

- Use a multi-Lambda repo with shared event contracts and isolated IAM roles.
- Keep the repo event-only and avoid synchronous API exposure.
- Treat duplicate/fraud detection as deferred and explicitly scaffolded.

## Open Items

- Confirm the actual .NET 10 SDK pin to use in this environment.
- Align with the production deployment strategy for Lambda packaging and Terraform.
