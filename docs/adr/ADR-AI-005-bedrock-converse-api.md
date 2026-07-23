# ADR-AI-005: Bedrock Model and API Surface for Enrichment

- **Date:** 2026-07-23
- **Status:** Accepted

## Context

E-03 needed to pick which Bedrock model to target and which .NET SDK API surface to call it through. CLAUDE.md's pre-implementation description said "invokes Bedrock (`InvokeModel`)", written speculatively before any real research.

## Options Considered

- **A — Raw `InvokeModel` with a provider-specific JSON body (Anthropic's Messages API shape).** What CLAUDE.md originally assumed. Requires hand-rolling the exact Anthropic request/response JSON, brittle if the model or provider changes, no built-in multimodal content-block abstraction.
- **B — Converse API (`IAmazonBedrockRuntime.ConverseAsync`).** Provider-agnostic: `Message`/`ContentBlock`/`ToolConfig` abstractions work across any Converse-supported model. Confirmed as the current .NET SDK's intended surface for multimodal + tool-use via Context7 (`/aws/aws-sdk-net`) research during Design.
- **C — Bedrock Agents / Knowledge Bases.** Overkill for a single-shot image-to-description-and-tags task with no retrieval or multi-turn state; adds infrastructure (agent definitions, action groups) with no corresponding benefit here.

## Decision

Option B: Converse API, targeting **Claude Sonnet 5** via its cross-region inference profile ID `us.anthropic.claude-sonnet-5` (confirmed current via web search during Design, July 2026 AWS/Anthropic documentation). `BedrockEnrichmentClient` wraps `ConverseAsync`, passing the image as a `ContentBlock.Image` (`ImageBlock.Source.Bytes`), and forces structured output via `ToolConfig`/`ToolChoice` (see ADR-AI-006 for why).

All Bedrock API shapes used (`ContentBlock`, `ImageBlock`, `ToolConfiguration`, `ToolSpecification`, `Amazon.Runtime.Documents.Document.AsDictionary/AsList/AsString`) were confirmed against the restored `AWSSDK.BedrockRuntime` 4.0.100.6 / `AWSSDK.Core` 4.0.100.8 package XML doc files — Context7 could name the types but not their exact members, so the package's own XML docs were the deciding source, per this repo's established `Amazon.Lambda.S3Events` convention (CLAUDE.md).

## Consequences

- CLAUDE.md's Architecture section needs correcting from "`InvokeModel`" to "Converse API" (done alongside this ADR).
- The model ID (`us.anthropic.claude-sonnet-5`) is a cross-region inference profile, not a bare model ID — whether this repo's Lambda region (`sa-east-1`, per `iac/terraform/variables.tf`'s `aws_region` default) can call it directly or needs explicit cross-region routing is unconfirmed and tracked as an open item (`.specs/features/e03-enrichment-pipeline/design.md`'s Open Questions).
- Switching models later (e.g. a future Claude release) is a one-constant change in `BedrockEnrichmentClient` (`ModelId`), not a request-shape rewrite — the Converse API's provider-agnostic shape is what makes this cheap.
- `AWSSDK.BedrockRuntime`'s pinned version (`4.0.14.0`) was stale on NuGet.org before this work — bumped to `4.0.100.6`, confirmed against the live flat-container API, same recurring pattern CONCERNS.md already flags from E-02.
