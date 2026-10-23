# TheSingularityWorkshop.CiAi.CogD: CiAi Integration Sample

## Intended role
Policy cog; test allow/deny matrix and prove package publishing remains denied.

This is a repository-specific integration proposal. Verify the role, workflow inventory, and source contracts before making it a required check.

## Evidence CiAi should consume
- Repository/ref, tested commit SHA, workflow file/event, run ID/attempt, status/conclusion, timestamps.
- Job/step results and direct provider log/artifact links.
- Exact resolved dependency SHAs/package versions, not just moving branch names.
- Explicit capability declarations and evidence for tests, build, docs, package, deployment, and benchmarks where applicable.
- Distinct successful, failed, cancelled, skipped, not-triggered, unavailable, and unknown states.

## How to consume CiAi
1. Existing GitHub Actions/provider CI remains the executor and authoritative evidence source.
2. Declare purpose, API/packages, capabilities, dependencies, supported runtimes, and examples.
3. Publish normalized evidence tied to commit and run attempt.
4. Use CiAi to correlate runs/dependencies and summarize compatibility with traceable source links.
5. Keep repository-specific checks and decisions in the repo/provider workflow.
6. Integrate read-only/advisory first. No package publication or deployment authority is granted to CiAi.

## Repository-specific priority
Explicit policy matrix for every operation; publishing must remain denied regardless of request flags.

## Rollout and acceptance
- Inventory workflows, permissions, dependencies, and action pins.
- Normalize provider evidence without changing existing gates.
- Correlate jobs to tested commit and resolved dependency versions.
- Treat missing evidence as unknown, not as a pass/fail.
- Make duplicate delivery idempotent while preserving rerun attempts.
- Link every summary to authoritative provider evidence; keep privileged actions out of CiAi.
