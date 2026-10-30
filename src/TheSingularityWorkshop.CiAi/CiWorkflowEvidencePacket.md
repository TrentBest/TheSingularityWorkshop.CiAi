# CiWorkflowEvidencePacket

**User moniker:** Exact-Revision Evidence Envelope

## Purpose

`CiWorkflowEvidencePacket` is a versioned, immutable JSON envelope for one exact GitHub Actions workflow run attempt. It carries repository, workflow, branch/ref, full source SHA, event, run/attempt identity, provider run URL, observation timestamp, job/step facts, discovery completeness, job completeness, and artifact references.

## Trust boundary

- The packet preserves observed CI facts; it does not execute repairs or grant repository capabilities.
- Every job must belong to the packet's exact run ID and attempt.
- Every run/job URL must be an HTTPS GitHub URL for that exact run.
- Missing or truncated discovery/job evidence remains incomplete.
- A successful workflow summary is not enough: every observed job and step must be completed successfully before `IsSuccessful` is true.
- `BlockingReasons` uses stable machine-readable codes; it does not convert diagnostics into instructions.
- Artifact references are references only. This contract does not claim that artifact bytes were downloaded, hashed, retained, or independently verified.
- A packet is not a release verdict, merge approval, publication authorization, or deployment authorization.

## JSON contract

- Schema: `ciai.workflow-evidence.v1`
- Contract version: `1`
- Serialization uses camelCase property names and string enum values.
- Unknown schema versions are rejected by `FromJson`; callers must not silently reinterpret a future packet.

## Current boundary

The existing GitHub Actions run and job collectors remain read-only and retain each run attempt independently. This packet is the normalized handoff layer above those observations. Durable delivery, dispatch/rerun requests, authenticated callback processing, artifact inventory/digest verification, and Foundry lifecycle integration remain separate work items; this type does not claim those systems exist.
