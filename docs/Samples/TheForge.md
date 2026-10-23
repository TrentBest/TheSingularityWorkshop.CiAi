# TheForge: CiAi Integration Sample

## Intended role
Workshop forge/showcase; validate user flows and generated outputs.

This proposal must be checked against current source and workflow files before being treated as a repository contract.

## Evidence to expose
- Repository identity, ref, tested commit SHA, workflow path, event, run ID/attempt, status/conclusion, timestamps.
- Job/step outcomes and direct provider log/artifact links.
- Resolved SHA/version for every sibling checkout or package dependency.
- Capability labels for build, tests, docs, package, host/platform checks, deploy, or benchmarks.
- Distinct missing, not-triggered, skipped, cancelled, unavailable, failed, unknown, and successful states.

## Consumption model
1. Existing CI remains the executor and authoritative evidence provider.
2. Declare purpose, package/API surface, runtimes, dependencies, capabilities, and sample entry points.
3. Publish a normalized evidence record tied to commit and run attempt.
4. Let CiAi correlate runs, dependency revisions, and declared compatibility; always link conclusions to source evidence.
5. Keep repository-specific validation in its own workflow.
6. Begin read-only/advisory. Do not authorize publishing, deployment, or merge blocking through this integration.

## Priority checks
Clean-checkout build/test, domain-specific contract tests, consumer compatibility, and sample execution.

## Acceptance criteria and rollout
- Inventory workflows and dependency edges first; do not infer a missing workflow is a failure.
- Correlate lanes to the same tested commit and resolved dependency SHAs.
- Treat a run as successful only when provider status is completed and conclusion is success.
- Keep retries distinguishable and duplicate deliveries idempotent.
- Every summary links to authoritative provider evidence.
- Add compatibility evaluation and advisory reporting only after evidence normalization is proven.
