# Conventions

- Prefer thin entrypoints and testable handler classes.
- Keep shared contracts versioned and additive-only.
- Apply least-privilege IAM and explicit event-driven boundaries.
- Align with the existing RentifyX service conventions for `.editorconfig`, `Directory.Build.props`, and central package versioning.
- Reusable cross-function primitives (idempotency store, generic Kafka event publisher) live in `src/Shared`, not duplicated per function — see `Shared/Idempotency/IIdempotencyStore` and `Shared/Kafka/IEventPublisher<T>`, both built for Moderation but intended for Enrichment too.
- Always verify NuGet package versions against `https://api.nuget.org/v3-flatcontainer/<id>/index.json` before pinning in `Directory.Packages.props` — never guess a version number.
