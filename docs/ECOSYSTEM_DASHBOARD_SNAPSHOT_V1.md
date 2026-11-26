# Ecosystem dashboard snapshot v1

**Status: contract/schema draft; no live cross-repository producer or dashboard consumer is implemented.**

The dashboard, compact SVG, JSON download, and future host-consumable endpoint must all be projections of one immutable snapshot. This contract deliberately keeps three independent facts separate:

- **Lifecycle** — FSM_Execution is authoritative. CiAi must not infer `OFF` from missing telemetry.
- **Dependency readiness** — CiAi's validated dependency graph and exact upstream/package evidence are authoritative.
- **CI outcome** — provider workflow/run evidence is authoritative for the observed attempt.

A repository can be awaiting human approval while its dependencies are ready; a successful CI run does not by itself establish lifecycle completion or dependency readiness.

## Contract

The JSON Schema is [`schemas/ciai.ecosystem-dashboard-snapshot.v1.schema.json`](../schemas/ciai.ecosystem-dashboard-snapshot.v1.schema.json).

Required top-level facts:

| Field | Meaning |
|---|---|
| `schemaVersion` | Fixed contract discriminator: `ciai.ecosystem-dashboard-snapshot.v1` |
| `snapshotId` | Stable identifier for this materialized observation |
| `generatedAt` | UTC/offset-aware generation timestamp |
| `freshness` | `CURRENT`, `STALE`, `UNKNOWN`, or `UNAVAILABLE` |
| `digestAlgorithm` | Fixed to `SHA-256` |
| `snapshotDigest` | SHA-256 of canonical snapshot content with this field omitted |
| `sourceEvidenceDigest` | SHA-256 of the canonical ordered source-evidence reference set |
| `sourceEvidence` | Safe links and timestamps for the evidence used |
| `repositories` | Repository identity and the three separate status dimensions |
| `dependencies` | Typed directed edges, required/optional distinction, and exact resolution evidence |
| `buildWaves` | Deterministic topological waves; empty when no valid order can be established |
| `diagnostics` | Explicit cycle, ambiguity, missing evidence, stale input, and validation findings |

Canonicalization for both digests is UTF-8 JSON with object properties sorted ordinally, array order preserved, no insignificant whitespace, and timestamps normalized to UTC ISO 8601. The snapshot digest is computed after omitting only `snapshotDigest`. The source evidence digest is computed over the canonical `sourceEvidence` array. Producers must reject duplicate evidence IDs and must not emit a digest for a payload that fails schema and semantic validation.

## Fail-closed interpretation

- Missing, stale, contradictory, or unavailable lifecycle evidence maps to `UNKNOWN`, `STALE`, `CONTRADICTORY`, or `UNAVAILABLE`; never to `OFF`.
- A lifecycle `OFF` value is valid only when explicitly supplied by FSM_Execution-authoritative evidence.
- A dependency is `READY` only when its required upstream is resolved to the exact full commit SHA or exact published package identity/version and the applicable evidence is complete, current, and unambiguous.
- A successful workflow run is not interchangeable with dependency readiness or lifecycle state.
- Required edges gate consumers. Optional/tooling edges remain visible but do not silently become required.
- Cycles or ambiguous graph declarations produce diagnostics and no executable build sequence. Independent, unblocked repositories may still appear in valid waves.
- Unknown or incomplete inputs remain visible. They must never be coerced into green/ready.
- Public snapshots contain safe identifiers, status, timestamps, and public evidence links only. Never include secrets, prompts, raw logs, private issue content, or model reasoning.

## Projection invariants

1. One validated snapshot is the input to every view.
2. HTML/Blazor, SVG, JSON, badges, and host adapters do not compute their own status.
3. Every view displays the snapshot identity, freshness, and digest (or a short digest prefix with a link to the complete JSON).
4. Every status claim can be traced through evidence IDs to source links.
5. Synthetic fixtures are clearly marked as fixtures and are never labelled live.
6. A missing snapshot produces a visible unavailable state and a useful static fallback—not a fabricated empty/healthy graph.

## Deliberate non-goals for v1

This schema does not authorize dispatch, approval, repository mutation, publishing, deployment, or any other command. It does not prescribe a live polling service, database, event stream, or public endpoint. Those require separately reviewed producers, trust boundaries, retention, and host policy.

## Current implementation boundary

The v1 schema is the first contract artifact only. The producer, canonical digest implementation, semantic validator, deterministic graph planner, fixture suite, GUI-backed dashboard, SVG/JSON renderers, and failure-safe Pages assembly remain outstanding. Do not describe the dashboard as live until an exact validated snapshot is produced and its source evidence is independently verified.
