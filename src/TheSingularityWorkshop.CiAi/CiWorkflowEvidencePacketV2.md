<!-- The Singularity Workshop animated moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# CiWorkflowEvidencePacketV2

**Contract:** `ciai.workflow-evidence.v2`  
**Schema:** `schemas/ciai.workflow-evidence.v2.schema.json`

## Purpose

v2 is the authoritative exact-run envelope intended for task lifecycle and repair-gate evaluation. It wraps the independently observed `ciai.workflow-evidence.v1` run/provenance packet with the exact required provider jobs registered by the trusted host before dispatch.

The v1 packet remains independently versioned and is not silently reinterpreted as v2. Diagnostic `ciai.test-results.v1` remains separate and can enrich an already-authorized failure; it cannot create a repair attempt or satisfy a required check.

## Required-check binding

The trusted host supplies one or more `CiWorkflowRequiredCheckBinding` values before dispatch and supplies that same expected set again when calling `FromJson` on receipt or reload:

- `Key`: stable machine-readable required-check identity registered in the dispatch mapping
- `JobName`: exact provider job display name to match in the observed run attempt

The producer matches each binding against the provider-observed job list:

- exactly one match → `state: observed`, with job ID, status, conclusion, evidence URL, and provider timestamps
- no match → `state: missing`
- multiple matches → `state: ambiguous`

Missing and ambiguous observations are retained explicitly and make `EvidenceComplete` false. The producer does not guess a job, accept a similarly named check, or infer success from the overall workflow conclusion.

## Decision semantics

- `EvidenceComplete` requires complete run discovery, complete job collection, a terminal run, and exactly one terminal observation for every required check, including provider start/completion timestamps.
- `IsSuccessful` additionally requires a trusted workflow definition, verified candidate checkout, a successful run, and success for every required check.
- `HasActionableDiagnostics` requires complete evidence, trusted workflow and checkout provenance, a failed workflow conclusion, and at least one failed required check. If any required check has a non-code terminal conclusion such as cancellation or timeout, the packet does not classify the attempt as actionable.
- `DiagnosticsFingerprint` is a deterministic SHA-256 over the ordered required-check identity, job ID, status, and conclusion. It is intended to detect repeated failure signatures, not to replace the exact payload digest.
- `PayloadSha256` is the SHA-256 of the exact compact UTF-8 v2 JSON payload.

## Trust and durable reload

The v2 JSON does not serialize a workflow-trust decision. `FromJson` requires the receiving host to supply the result of its current protected-workflow policy check. The embedded v1 packet is independently re-parsed and digest-checked, and the outer v2 payload is checked against its trusted durable-store digest.

A digest proves byte integrity, not sender identity, workflow trust, or authorization. The receiving host must still bind repository, run ID/attempt, workflow path/ref/SHA, candidate ref/SHA, and required-check keys to its persisted dispatch intent.

## Current implementation boundary

The library contract, v2 JSON serializer/parser, schema, host-bound required-check matching, trust re-evaluation, durable idempotent v2 outbox, shared transport receipt acknowledgement, and contract tests are implemented on the current integration branch. The live provider-observation adapter, v2 artifact publication, authenticated metadata notice delivery, and Execution-side v2 consumer still need to be wired and proven end to end.

No v2 packet authorizes repository writes, merge, package publication, release, or deployment. Human approval and a fresh narrow host capability remain mandatory for repository side effects.
