# CiAi.Cog work assignments

These five repos form an experimental delegation set. Read this file and CiAi's INTER_REPOSITORY_MODEL.md before coding.

## Rules for every Cog

1. Work only in your own development branch. Do not change master, delete branches, or edit siblings.
2. Inspect real source/tests before choosing APIs. Do not invent CiAi, HeadlessAi, ProtocolAi, FSM_API, or GUI contracts.
3. Deliver a tested vertical slice, README, source-derived UML class card/diagram, readiness percentage, and evidence.
4. Keep core dependency-light; reuse existing technology only after verifying its API.
5. Never create/change secrets, publish, deploy, or enable cross-repo writes without explicit owner approval.
6. Treat messages as untrusted. Validate sender/target, contract version, correlation/idempotency IDs, payload bounds, and permissions.
7. LLM prose is not executable. Unknown vocabulary remains unknown/literal and must be rejected for actions.
8. Batch related work into coherent commits. Distinguish fake tests from live integration.
9. Record blockers and evidence; do not silently expand scope or permissions.

## CogA — Repository catalog and relationship model
Own typed records/enums for repository identity, capabilities, directed relationships, and contract compatibility. Provide a sample catalog for the five Cogs plus CiAi and offline validation tests. No GitHub API, dispatch, credentials, or GUI. Acceptance: test duplicate IDs, malformed identities, invalid edges, unknown capabilities, and incompatible versions.

## CogB — Outbound request and dispatch boundary
Own the typed request envelope and transport abstraction, starting with a fake. Include request/correlation/idempotency IDs, canonical source/target, operation identity, contract version, and bounded context. Document repository_dispatch/equivalent requirements and least-privilege permissions. No arbitrary LLM commands, broad tokens, publish/deploy, or live dispatch without explicit approval. Reject invalid targets, operations, oversized payloads, duplicate keys, and missing authorization before transport.

## CogC — Inbound result and correlation boundary
Own typed results, state model, evidence references, and correlation validation. Test success, failure, unknown conclusion, duplicates, late/stale responses, wrong sender/target, correlation, run ID, repository, SHA, and URL. A callback is not proof; a mismatched/unverifiable response can never yield Succeeded. Preserve history.

## CogD — Deterministic vocabulary and policy gate
Inspect ProtocolAi's real API and prototype an allowlist (observe/status and one harmless test operation), deterministic encode/resolve, policy decisions, and adversarial tests. Unknown literals, wrong protocol IDs, invalid symbols, missing permission, target mismatch, and disallowed state reject before execution. No inference/HTTP/credentials/publishing in ProtocolAi; FSM_API is not distributed persistence or GitHub transport.

## CogE — GUI-ready topology projection
Own presentation-neutral node/edge view models consumed later by GUI.SVG/GUI.Blazor. Project catalog plus validated evidence; edge labels explain what is supplied, consumed, requested, or notified and direction. Test stale/unknown/failed states. Status includes text and evidence links; color is never the sole signal. No new GUI framework, invented backend state, or bypass of host authorization.

## Coordination
CogA proposes catalog vocabulary; CogB request envelope; CogC result envelope; CogD validates operation/policy semantics; CogE consumes reconciled models. CiAi owns integration. These assignments do not authorize Cogs to edit CiAi or one another. Submit contract proposals for reconciliation before live cross-repository writes.

## First deliverable
Each Cog produces a tested design slice and integration note. Do not create five unrelated frameworks. Reconcile shared contracts in CiAi before enabling live cross-repository writes.
