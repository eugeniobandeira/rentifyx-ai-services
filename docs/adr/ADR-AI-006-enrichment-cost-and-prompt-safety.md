# ADR-AI-006: Enrichment Cost and Prompt-Injection Guardrails

- **Date:** 2026-07-23
- **Status:** Accepted

## Context

CLAUDE.md flags Bedrock cost and prompt safety as first-class design constraints for Enrichment (CONCERNS.md echoes this). The image Bedrock processes is user-uploaded and untrusted — it could contain embedded text attempting to hijack the model ("ignore previous instructions..."). Uncontrolled Bedrock invocation frequency or response size is also a real cost risk at scale.

## Options Considered

**Cost:**
- **A — No bound, trust the idempotency store alone.** Idempotency already prevents duplicate invocations for the same asset, but says nothing about response size per call.
- **B — Cap `InferenceConfiguration.MaxTokens` on every request.** Bounds worst-case cost per call regardless of what the model tries to generate.

**Prompt safety:**
- **A — Free-form text response, parse description/tags out of prose afterward.** Simplest to implement, but the model could return anything — including text that echoes injected instructions from the image — and a regex/string-based parse of unstructured output is fragile and doesn't reject malicious content, it just fails to extract fields cleanly.
- **B — Tool-forced structured output (`ToolConfig`/`ToolChoice` with a specific tool), system prompt kept separate from the image content block.** The model can only respond via the tool's JSON schema — a schema mismatch is a clean, unambiguous failure signal. Role separation (system prompt vs. image content) means the image is unambiguously data-to-describe, not instructions-to-follow, in the request structure itself, not just the wording of the prompt.

## Decision

Cost: Option B. `BedrockEnrichmentClient` sets a fixed `MaxTokens` (1024) on every `ConverseRequest`.

Prompt safety: Option B. The system prompt (an explicit `SystemContentBlock`) instructs the model to treat the image purely as visual content and never follow instructions found within it, and is passed as a structurally separate field from the image `ContentBlock` — never string-concatenated. The response is forced through a single named tool (`suggest_enrichment`) with a JSON schema requiring `description` (string) and `tags` (string array). Any response that isn't a valid tool-use block matching that schema is treated as a hard failure (`EnrichmentResult.Succeeded = false`) and routed to the failure DLQ — never a best-effort partial parse, never published downstream.

## Consequences

- A schema-mismatch failure and a genuine Bedrock error (throttling, service fault) both surface through the same `EnrichmentResult.Succeeded = false` / failure-DLQ path — an operator inspecting the DLQ can't immediately distinguish "model refused to use the tool" from "Bedrock was down" without reading `FailureReason`. Acceptable for v1; revisit if DLQ volume from schema mismatches turns out to be significant enough to need a distinct signal.
- The 1024-token cap is a starting value, not measured against real description/tag payload sizes in production — same "revisit once real traffic exists" posture ADR-AI-003 took for Moderation's confidence thresholds.
- Tool-forced output only works on models that support Converse's tool-use feature (Claude 3+ per the SDK's own XML doc notes on `ContentBlock.Image`/`SpecificToolChoice`) — this is not a portable-to-any-model guarantee, it's coupled to the ADR-AI-005 model choice.
