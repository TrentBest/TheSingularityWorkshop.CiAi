# CI Workflow Sample Audit

Status: initial source-level audit; not a market survey.
Branch: `development`
Purpose: turn real workflow examples into evidence for CiAi's domain model. The examples below are pinned to the exact workflow blob revisions listed here. This document distinguishes observed workflow behavior from proposed CiAi abstractions.

## Method and confidence

For each sample, record triggers, execution graph, permissions, identity/trust boundaries, artifacts, failure behavior, and the domain concepts that recur. The sample is deliberately small and is biased toward our own .NET/FSM/CiAi ecosystem. It is useful for discovering design requirements, not for estimating market share or declaring a universal ranking.

This pass reads workflow YAML only. It does **not** yet inspect every referenced project, script, third-party action implementation, branch protection rule, repository secret configuration, or workflow run. Findings dependent on those are marked as questions, not conclusions.

## Sample register

| Sample | Exact workflow revision | What it represents |
|---|---|---|
| CiAi contract verification | [ci.yml](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/.github/workflows/ci.yml), blob `3772eb7a0ceae345490d43f915f686adccc8688e` | Restore/build/contract tests/package validation/artifact handoff |
| CiAi release candidate observation | [observe-release-candidate.yml](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/.github/workflows/observe-release-candidate.yml), blob `3027d68e50df62db2f2b3350d32903ba7c5ea014` | Manually requested, read-only evidence gathering for an exact candidate SHA |
| CiAi vertical ecosystem contract | [cog-ecosystem.yml](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/.github/workflows/cog-ecosystem.yml), blob `10f973a571b4fcbad700b4d95a1f4dcd5f840fba` | Cross-repository compatibility test at pinned dependency revisions |
| FSM_API .NET pipeline | [dotnet.yml](https://github.com/TrentBest/FSM_API/blob/master/.github/workflows/dotnet.yml), blob `53394a12cf6eb5cf977a84449e6b635e62a29590` | Build/test, coverage upload, package artifacts, disabled documentation and publish jobs |

## Sample 1 — CiAi contract verification

### Observed workflow

- Triggers on pushes and pull requests targeting `development` or `master`, with a path filter, plus manual dispatch.
- Requests `contents: read`.
- Uses a concurrency group keyed by ref and cancels older in-progress runs in that group.
- Checks out source, installs .NET 8, restores and builds the solution, runs contract checks, packs a fixed alpha package version, checks expected package files and nuspec identity/version, then uploads package files as an artifact.
- Checkout, setup-dotnet, and upload-artifact actions are pinned to full commit SHAs.

### Domain requirements exposed

1. Trigger predicates are part of workflow intent, not incidental metadata.
2. Build/test/package are ordered stages with explicit prerequisites.
3. Package verification is a separate assertion after package creation; a successful pack command alone is insufficient evidence.
4. The artifact is a candidate for later inspection, not authorization to publish.
5. Cancellation policy is an explicit concurrency decision.

### Risks and unanswered questions

- The path filter is an allowlist. A future workflow-relevant file outside those paths may not trigger this workflow unless the filter is updated.
- The YAML checks package existence, README inclusion, and package ID/version; it does not itself prove archive integrity, package API compatibility, PDB correctness, provenance, or publication eligibility.
- This audit has not compared the workflow's expected package version with all version sources in the repository.

### Candidate CiAi vocabulary (proposal only)

`On(push | pull_request | manual)`, `When(paths, branches)`, `LimitConcurrency(key, cancelPrevious)`, `Run(restore)`, `Run(build)`, `Run(contractChecks)`, `Pack(packageIdentity)`, `VerifyArtifact(assertions)`, `PublishArtifact(retentionPolicy)`.

## Sample 2 — CiAi release candidate observation

### Observed workflow

- Manual dispatch requires a repository name and full candidate commit SHA.
- Requests `contents: read` and `actions: read`.
- Does not cancel an in-progress observation with the same repository/SHA key.
- Validates repository-name shape and a 40-character hexadecimal SHA, builds the observer, then collects workflow runs, attempts, jobs, and steps for the requested candidate.
- Uploads the raw observation report even after a failed job, if the report exists, with a 14-day retention period.

### Domain requirements exposed

1. Candidate identity should be explicit and immutable for an evaluation.
2. Input shape validation is not equivalent to proving that the commit exists, belongs to the requested repository, or is the commit actually associated with a particular workflow run.
3. Observation should be distinct from evaluation, authorization, and execution.
4. Evidence collection should preserve useful partial reports after failures, while labeling completeness and failure.
5. Re-running an observation may be useful; it should not silently mutate the candidate identity.

### Risks and unanswered questions

- The observer application must be inspected to determine exactly how it queries GitHub, paginates results, handles API errors/rate limits, matches the SHA, and represents missing/in-progress runs.
- `always()` allows upload after failure, but cannot upload a file that was never created; `warn` means absence of a report may not fail the workflow.
- A syntactically valid input does not guarantee semantic repository/commit validity. CiAi should expose that distinction in evidence and status.

### Candidate CiAi vocabulary (proposal only)

`ObserveCandidate(repository, commit)`, `ValidateIdentitySyntax()`, `ResolveIdentity()`, `CollectWorkflowEvidence()`, `RecordCoverageAndGaps()`, `PreservePartialEvidence(retention)`. This is an evidence pipeline, not a release command.

## Sample 3 — CiAi vertical ecosystem contract

### Observed workflow

- Runs for changes in integration/source/tests and selected build/configuration/docs paths, plus manual dispatch.
- Requests `contents: read`.
- Checks out CiAi and four companion repositories into separate directories.
- Each companion checkout uses an explicit commit SHA rather than a moving branch/tag.
- Restores, builds, and runs a vertical contract test project from the CiAi checkout.

### Domain requirements exposed

1. Multi-repository workflows need a dependency set with explicit identity and revision for each repository.
2. A passing local build is weaker than a verified cross-repository contract.
3. Reproducibility depends on more than the primary repository SHA.
4. Cross-repo tests need provenance: which exact component revisions formed the tested combination?

### Risks and unanswered questions

- The checked-out component SHAs are immutable, but this YAML alone does not show how those revisions are selected, refreshed, or checked for compatibility with the current CiAi commit.
- The trigger path filter should evolve with the integration contract and the files that define it.
- This workflow does not upload a manifest of the complete tested revision set as an artifact; whether logs are sufficient for the intended audit is a product question.

### Candidate CiAi vocabulary (proposal only)

`ComposeRevisionSet(primary, dependencies)`, `CheckoutExactRevisions()`, `VerifyCompatibility(contract)`, `EmitRevisionManifest()`. The manifest is a candidate improvement, not a claim about current behavior.

## Sample 4 — FSM_API .NET pipeline

### Observed workflow

- Runs on push and pull request to `master`, plus manual dispatch with a boolean `publish` input.
- Uses Ubuntu 24.04, .NET 8, a NuGet package cache, restore/build/test, coverage upload, package packing, DLL copying, and artifact uploads.
- Action references such as `actions/checkout@v7`, `actions/setup-dotnet@v6`, `actions/cache@v5`, and upload/download-artifact major tags are not immutable commit-SHA pins in this revision.
- Documentation generation is disabled by a constant `&& false`.
- NuGet publishing is also disabled by a constant `&& false`, despite the manual input and a job that requests `id-token: write` and uses NuGet trusted publishing.

### Domain requirements exposed

1. Caching is an optimization layer, not a correctness proof; restore/build/test should remain independently observable.
2. Coverage upload is a separate external integration with its own credential and failure policy.
3. Artifact naming/copying encodes target-framework expectations and can fail when the build matrix/framework set changes.
4. A manual intent flag, a job condition, and actual publish authorization are separate gates.
5. Disabled steps should be represented as intentionally disabled, not as completed capabilities.

### Risks and unanswered questions

- Floating action tags can change behavior without a workflow file change; a supply-chain policy may require immutable SHA pins.
- The workflow's copy commands assume specific output directories exist. The workflow needs a clear failure and compatibility story if target frameworks change.
- The coverage step has an external token and is configured to fail the job if upload fails. Whether this is the desired release policy should be explicit.
- Since publish is hard-disabled in this revision, it is not currently an active publishing path. The presence of the job is not evidence that publishing is enabled or tested.
- This audit did not run the workflow or verify that every referenced framework output is produced.

### Candidate CiAi vocabulary (proposal only)

`Cache(key, restoreKeys)`, `Test(coverage)`, `PublishCoverage(required)`, `Pack(targets)`, `CollectArtifacts(names)`, `RequireExplicitReleaseIntent()`, `RequireReleaseAuthorization()`, `ExecutePublishOnlyIf(allGuards)`.

## Cross-sample synthesis

The most useful recurring abstraction is not “a YAML step.” It is a **guarded, evidence-producing transition** with a defined identity, prerequisites, failure policy, and outputs.

Across these four examples, the shared domain concepts are:

- **Identity:** repository, commit SHA, dependency revision set, workflow run and attempt.
- **Eligibility:** trigger, branch/path filters, input validation, concurrency rules.
- **Execution graph:** ordered jobs/steps and explicit dependencies.
- **Evidence:** test results, package checks, workflow/job/step observations, coverage, artifacts.
- **Evaluation:** whether the evidence satisfies a stated policy.
- **Authorization:** explicit approval or a separately defined policy gate.
- **Execution:** actions with external side effects, especially publication/deployment.
- **Verification and audit:** observed outcome, revision provenance, partial evidence, retention.

These should not automatically become eight mandatory public classes. They are candidate domain invariants from which smaller composable APIs can be derived. A simple format-check workflow should remain simple; a release train should be able to express the full guarded lifecycle without pretending that a green check is permission to publish.

## Next audit steps

1. Inspect the CiAi observer implementation and its tests to validate the behavior inferred from `observe-release-candidate.yml`.
2. Inspect the package metadata/version source and scripts used by CiAi CI.
3. Add a small set of outside projects with materially different shapes: monorepo/release train, container build and deployment, and security/provenance gates. Pin each sample to a commit and keep selection bias explicit.
4. Update `CI_PRODUCT_ASSESSMENT.md` only when concrete source evidence changes a design hypothesis.
5. Turn repeated requirements into contract tests before presenting an API as implemented.

## Relationship to other documents

- [CI workflow domain catalog](CI_WORKFLOW_DOMAIN_CATALOG.md)
- [CI product assessment](CI_PRODUCT_ASSESSMENT.md)
- [CI user workflows](CI_USER_WORKFLOWS.md)
- [Release readiness](RELEASE_READINESS.md)

This document is an engineering notebook, not a claim that CiAi already implements the proposed API or that these samples represent the broader market.
