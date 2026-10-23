# TheSingularityWorkshop.MicroBundleDomain: CiAi Integration Sample

## Intended role
Domain contracts; validate schema/API compatibility and downstream consumer matrix.

This is a proposal, not a statement that integration is already implemented. Confirm actual workflows and source contracts before turning it into an enforcement rule.

## Evidence contract
- Canonical repository, ref, tested commit SHA, workflow file, trigger/event, run ID and attempt.
- Provider status/conclusion, timestamps, job/step outcomes, and direct logs/artifact URLs.
- Resolved SHA/version of each sibling checkout/package; branch refs alone are mutable.
- Declared capabilities and separate evidence for build, tests, docs, package, deployment, benchmarks, and platform-specific lanes.
- Missing workflow, not-triggered, skipped, cancelled, unavailable, unknown, failed, and successful remain distinct.

## How to consume CiAi
1. Keep existing provider CI as the authoritative executor.
2. Declare repository purpose, public packages/capabilities, dependency constraints, supported runtimes, and examples.
3. Emit normalized evidence tied to commit and run attempt.
4. Use CiAi to correlate lanes and dependency revisions, evaluate declared compatibility, and report traceable links.
5. Keep actual checks and release policy in this repository's workflows; CiAi should not duplicate test execution.
6. Roll out read-only/advisory first. No implicit publish, deploy, or merge-gate authority.

## Repository-specific evidence
Stable identity, schema invariants, compatibility declarations, and consumer matrix.

## Adoption stages
- Inventory active workflows, triggers, permissions, action pins, dependencies, and artifacts.
- Observe evidence without changing CI behavior.
- Correlate jobs to tested commit and exact dependency revisions.
- Evaluate compatibility declarations; missing evidence means unknown, not compatible.
- Publish advisory summaries linking every claim to provider evidence.

## Acceptance criteria
Successful requires provider status completed and conclusion success. Preserve rerun attempts and make duplicate delivery idempotent. Do not conflate benchmark movement with correctness. Do not allow CiAi to publish or deploy.
