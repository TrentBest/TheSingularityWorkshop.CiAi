# CiAi.Cog work assignments

These five repos form an experimental delegation set. Read this file and INTER_REPOSITORY_MODEL.md before coding.

## Rules for every Cog
1. Work only in your own development branch. Do not change master, delete branches, or edit siblings.
2. Inspect actual source/tests before choosing APIs; do not invent CiAi, HeadlessAi, ProtocolAi, FSM_API, or GUI contracts.
3. Deliver a tested vertical slice, README, source-derived UML class card/diagram, readiness percentage, and evidence.
4. Keep core dependency-light; reuse technology only after verifying its API.
5. Never create/change secrets, publish, deploy, or enable cross-repo writes without explicit owner approval.
6. Validate sender/target, contract version, correlation/idempotency IDs, payload bounds, and permissions.
7. LLM prose is not executable. Unknown vocabulary is rejected for actions.
8. Batch coherent changes; distinguish fake tests from live integration.
9. Record blockers/evidence; do not silently expand scope or permissions.

## CogA — Repository catalog
Own typed repository identities, capabilities, directed relationships, contract compatibility, sample catalog, and offline validation tests. No GitHub API, dispatch, credentials, or GUI. Test duplicate IDs, malformed identities, invalid edges, unknown capabilities, incompatible versions.

## CogB — Outbound request
Own a typed request envelope and transport abstraction, initially with an offline fake. Include request/correlation/idempotency IDs, canonical source/target, operation identity, contract version, bounded context. Document repository_dispatch/equivalent and least-privilege requirements. Reject invalid targets/operations, oversized payloads, duplicate keys, and missing authorization before transport. No arbitrary LLM commands, broad tokens, publish/deploy, or live dispatch without approval.

## CogC — Result/correlation
Own typed result envelopes, state model, evidence references, and correlation validation. Test success, failure, unknown conclusion, duplicate/late responses, wrong sender/target, correlation, run ID, repository, SHA, and URL. A callback is not proof; mismatched/unverifiable results can never become Succeeded. Preserve history.

## CogD — Deterministic vocabulary/policy
Inspect ProtocolAi's real API. Prototype a small allowlist, deterministic encode/resolve, policy decisions, and adversarial tests. Unknown literals, wrong protocol IDs, invalid symbols, missing permission, target mismatch, and disallowed state reject before execution. No inference/HTTP/credentials/publishing in ProtocolAi; FSM_API is not distributed persistence or GitHub transport.

## CogE — GUI-ready topology
Own presentation-neutral node/edge models for later GUI.SVG/GUI.Blazor consumption. Project catalog plus validated evidence. Edge labels explain what is supplied, consumed, requested, or notified and direction. Test stale/unknown/failed states. Status includes text and evidence links; color is never the sole signal. No new GUI framework, invented backend state, or bypass of host authorization.

## Coordination
CogA proposes catalog vocabulary; CogB request envelope; CogC result envelope; CogD validates operation/policy semantics; CogE consumes reconciled models. CiAi owns integration. Cogs do not edit CiAi or one another; submit contract proposals for reconciliation before live cross-repo writes.

First deliverable: tested design slice plus integration note. Do not build five unrelated frameworks.