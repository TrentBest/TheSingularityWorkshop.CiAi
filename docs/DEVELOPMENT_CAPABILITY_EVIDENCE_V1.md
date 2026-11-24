# Development Capability Evidence v1

## Purpose

A development capability is an exact, tested source capability offered by a provider repository. It is not a published NuGet package, release, deployment, or consumer authorization.

The first vertical slice is:

- Provider: `TrentBest/TheSingularityWorkshop.Physics`
- Capability: `physics.kinematics.prescribed-motion.v1`
- Capability version: `1`
- MicroBundle: ID `5203`, version `0.1.0-alpha.1`
- Consumer: `TrentBest/raWWar`

The provider declares the capability and emits a run-scoped evidence artifact. CiAi independently verifies the declaration against GitHub API metadata and the actual test artifacts.

## Trust binding

A valid evidence packet is bound to all of the following:

1. Provider repository and exact candidate source ref/SHA.
2. Workflow path, workflow ref/SHA, run ID, and run attempt.
3. Event name/ref/SHA and actual checkout ref/SHA. A PR merge ref must never be mislabeled as the checked-out source branch.
4. Exact external dependency checkout SHAs.
5. Required job name and successful terminal conclusion from GitHub's run/job API.
6. Required step IDs and successful outcomes.
7. Required artifact names, file digests, sizes, and MicroBundle identity.
8. Provider artifact ID, GitHub-reported SHA-256 digest, size, and non-expired state.
9. Policy ID/version and the exact expected source-file set.

The provider-generated JSON is untrusted input. A self-reported `identityValid`, `satisfied`, or SHA value is never sufficient. The artifact archive digest and job/run identity must come from independent GitHub API observations.

## Test-result artifacts

Each declared test-result artifact is a ZIP containing exactly:

- `results.trx`
- `provenance.json`

The separate archives allow the existing `CiTestResultArtifactArchive` verifier to validate each archive digest and provenance without weakening its strict two-entry limit. CiAi then parses the TRX with `TrxTestResultParser`; provider diagnostics remain untrusted data.

The larger development-capability artifact also carries the MicroBundle, both TRX files, workflow provenance, source hashes, and the complete versioned evidence envelope.

## Readiness semantics

- **Waiting:** the provider workflow/run has not reached a terminal state. Persist the consumer candidate and keep it dormant; do not allocate an LLM turn or poll.
- **Satisfied:** exact terminal provider evidence is independently verified, every required check passed, all required artifacts are present, and the source candidate matches the consumer's declared capability requirement.
- **Blocked:** terminal provider failure, invalid/revoked evidence, mismatched source/dependency/workflow identity, failed required checks, expired/missing artifacts, or policy violation.

An envelope marked `incomplete` after a terminal run is not a neutral wait; it is a failed evidence contract and must not satisfy a gate.

## Candidate versus protected-push evidence

A green PR run proves only that the exact PR candidate passed its declared checks. It is not trusted protected-push evidence.

The parser returns `IsProtectedPushEvidence` only when the host's branch policy marks the development branch protected and all of these hold:

- event is `push`;
- source ref and event ref are exactly `refs/heads/development`;
- event SHA equals the candidate source SHA;
- checkout SHA equals the candidate source SHA;
- exact workflow/run/attempt/job/artifact bindings all validate.

The host must still use its current branch-protection policy and revocation state. A valid candidate artifact cannot authorize merge, package publication, release, deployment, or protected-branch writes.

## Existing gate integration

`CiAiDevelopmentCapabilityEvidenceParser` validates the versioned envelope and independently observed metadata. It does not create a second scheduler or gate engine. Its verified result is intended to become a leaf evidence type in the existing composite AND/OR pipeline evaluator, alongside (but never conflated with) `PublishedDependencyEvidence`.

Published-package readiness and source-capability readiness are distinct evidence kinds. Never mark a source capability as a published package, and never let a successful development capability imply NuGet publication.
