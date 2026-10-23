# TheSingularityWorkshop.Renderer.Benchmarks: CiAi Integration Sample

## Intended role
Renderer benchmarks; capture reproducible environment and keep performance trends separate from functional correctness.

This proposal must be verified against the current source and workflows before becoming an enforced contract.

## Evidence CiAi should consume
- Repository, ref, tested commit SHA, workflow path, trigger, run ID/attempt, provider status/conclusion, and timestamps.
- Job/step results with authoritative log/artifact links.
- Exact resolved SHA/version of each sibling checkout/package.
- Explicit capability evidence for build, test, package, docs, benchmark, platform, and deployment lanes.
- Separate states for success, failure, cancellation, skip, not-triggered, unavailable, and unknown.

## How this repository should consume CiAi
1. Keep current provider CI as the authoritative executor.
2. Declare purpose, packages/API, supported runtime, capabilities, dependency constraints, and examples.
3. Expose normalized run evidence tied to a commit and attempt.
4. Let CiAi correlate the evidence and evaluate declared compatibility; link all findings to source runs.
5. Keep domain-specific checks in this repository; CiAi reports rather than re-executes them.
6. Start read-only/advisory. Do not grant publish/deploy rights or require CiAi as a merge gate prematurely.

## Priority evidence
Runtime, machine, workload/input, commit, repeat count, and baseline metadata; timing is advisory and distinct from correctness.

## Rollout
- Inventory actual workflows, permissions, action pins, dependencies, and artifacts.
- Observe and normalize before altering gates.
- Correlate each result to tested commit and resolved dependencies.
- Treat missing evidence as unknown, not success or failure.
- Report advisory summaries with direct evidence links; keep privileged operations outside CiAi.

## Acceptance criteria
A run is successful only if provider status is completed and conclusion is success. Reruns remain distinguishable, duplicate events are idempotent, and every compatibility claim cites exact evidence.
