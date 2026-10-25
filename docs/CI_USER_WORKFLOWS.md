# CI User Workflows and Fluent Vocabulary

**Status:** Product and API discovery document. It describes real user intent and target vocabulary; it does not claim these capabilities are implemented in CiAi.

For the broader complexity-ranked inventory—from governed releases and provenance to basic PR checks—see the [CI Workflow Domain Catalog](CI_WORKFLOW_DOMAIN_CATALOG.md).

## Purpose

CiAi's fluent API should be designed from the work people actually ask CI to do. It should read like a repository's policy and workflow, not like a thin wrapper around shell commands or one provider's YAML syntax.

A CI pipeline usually answers five questions:

1. **When should this happen?** Events, branch/tag filters, schedules, manual dispatch, and reusable workflow calls.
2. **What work should happen?** Restore, build, test, lint, scan, package, publish, deploy, or a repository-specific operation.
3. **What must be true first?** Dependencies, conditions, required checks, environment availability, and prior results.
4. **What evidence should be retained?** Logs, test results, coverage, artifacts, digests, provenance, and provider run identity.
5. **Who or what is allowed to authorize consequential actions?** Repository protection, environment approval, an explicit owner decision, or another trusted policy authority.

These concerns should be represented separately. A recipe describes intent; a run produces results and evidence; evaluation interprets that evidence against a policy; authorization grants permission; an executor performs the operation.

## Common user workflows

### 1. Validate a proposed change

**What users say:** “Whenever a pull request is opened or updated, restore dependencies, build, run tests, and report failures. Do not publish anything.”

**Vocabulary:** `OnPullRequest`, `Restore`, `Build`, `Test`, `Report`, `FailIf`.

**Important semantics:** A failed or missing required check blocks the recipe's success. This workflow is validation-only; no publishing capability should be present.

Illustrative target syntax (not implemented API):

```csharp
CiAi.Create("owner/repository")
    .OnPullRequest()
    .Restore()
    .Build()
    .Test()
    .Report();
```

### 2. Validate a change after it reaches the main branch

**What users say:** “When a change lands on main, run the same checks against the committed revision.”

**Vocabulary:** `OnPushToMain`, `Restore`, `Build`, `Test`.

**Important semantics:** The recipe and its report must identify the exact commit. A green run for another commit is not evidence for this one.

### 3. Test across supported configurations

**What users say:** “Run the tests on every supported .NET version and operating system.”

**Vocabulary:** `Matrix`, `OnFrameworks`, `OnOperatingSystems`, `ForEach`, `AggregateResults`.

**Important semantics:** The matrix dimensions and supported values are explicit; the report identifies each combination. Missing matrix legs must not be mistaken for passing legs. Matrix expansion and scheduling are execution concerns, not just display syntax.

### 4. Reuse work and promote an artifact

**What users say:** “Build once, test that artifact, and promote that exact artifact to the next environment.”

**Vocabulary:** `Pack`, `PublishArtifact`, `VerifyArtifact`, `Promote`, `DeployTo`, `Retain`.

**Important semantics:** Artifact identity, digest, source commit, and provenance travel with the artifact. A later stage must not silently rebuild different bits and call them the same candidate.

### 5.1 Verify the complete package output set

**What users say:** “When we pack a NuGet release, confirm that both the package and symbol package for this exact ID and version are present in the artifact we retained.”

**Vocabulary:** `ExpectNuGetPackage`, `VerifyArtifactInventory`, `RequireFiles`, `ReportMissingArtifacts`.

**Important semantics:** A successful `dotnet pack` step is not proof that every expected output was uploaded or retained. For a symbol-enabled NuGet candidate, inventory verification should require both `.nupkg` and `.snupkg` for the exact package ID/version. This is a name-and-presence check only; it does not replace package-content inspection, PDB verification, source-SHA correlation, or artifact digest verification.

The first implementation slice is `NuGetPackageArtifactVerifier.Verify(packageId, version, observedRelativePaths)`. It returns a deterministic report and fails completeness when either exact-version output is absent. It does not download artifacts, inspect archives, or publish packages.

### 5. Prepare a release candidate

**What users say:** “For a release tag, run the checks, create the package, inspect the package metadata, and produce a candidate report.”

**Vocabulary:** `OnTag`, `PackNuGet`, `VerifyPackage`, `CreateCandidate`, `AttachEvidence`.

**Important semantics:** Creating and inspecting a candidate is not publishing it. The candidate report should state exactly which package ID, version, source commit, and digest were verified.

### 6. Publish or deploy with explicit authorization

**What users say:** “Publish this verified package only after an authorized owner approves this exact candidate.”

**Vocabulary:** `RequireApproval`, `Authorize`, `PublishNuGet`, `DeployTo`, `RequireEnvironmentApproval`.

**Important semantics:** A passing build, an Observer report, a model recommendation, or a custom step returning `true` is not approval. Authorization must be explicit, auditable, scoped to the action and bound to the repository, source commit, package/version/digest, target feed or environment, and run/plan identity. If approval is missing, stale, rejected, or mismatched, execution fails closed. Provider environment protections may be used; CiAi must not pretend its own metadata flag is an approval system.

### 7. Run expensive work only when needed

**What users say:** “Run the documentation check only if documentation changed; run integration tests only after the unit tests pass.”

**Vocabulary:** `WhenChanged`, `If`, `After`, `DependsOn`, `OnlyIf`, `ContinueOnFailure`.

**Important semantics:** Conditions and dependencies are distinct. A dependency says what must finish first; a condition says whether a step is eligible. Skipped, unknown, and failed are distinct outcomes. Failure-handling choices must be explicit and visible in the report.

### 8. Parallelize independent work

**What users say:** “Run linting, unit tests, and security checks in parallel, then combine their results.”

**Vocabulary:** `InParallel`, `Join`, `AggregateResults`, `MaxConcurrency`.

**Important semantics:** Parallelism is a dependency graph, not merely sequential source-code formatting. Reports retain each branch's outcome and make incomplete work visible.

### 9. Control duplicate and scheduled runs

**What users say:** “Run the nightly compatibility suite. If a newer commit arrives, cancel obsolete validation for the same branch.”

**Vocabulary:** `OnSchedule`, `Every`, `ConcurrencyGroup`, `CancelSupersededRuns`, `TimeoutAfter`, `Retry`.

**Important semantics:** Cancellation, timeout, and retry are distinct from ordinary failure. Retry policy must not repeat non-idempotent or privileged actions without an explicit safe policy.

### 10. Diagnose and audit a run

**What users say:** “Tell me which checks ran, which were skipped, why something failed, and what exact commit and artifact were involved.”

**Vocabulary:** `Observe`, `CollectEvidence`, `Evaluate`, `Explain`, `WriteReport`, `AttachArtifact`.

**Important semantics:** Provider facts are preserved with source identity and provenance. Observation is not evaluation; evaluation is not authorization. Unknown or incomplete evidence stays unknown/incomplete rather than being promoted to success.

### 11. Reuse workflows across repositories

**What users say:** “Use our standard library validation recipe for every package, with each repository supplying its own build and test details.”

**Vocabulary:** `Use`, `ImportRecipe`, `WithParameters`, `Extend`, `Override`.

**Important semantics:** Reuse needs stable recipe identity and explicit parameter contracts. Overrides must be inspectable and should not silently weaken inherited security or release requirements.

## Vocabulary design rules

1. Prefer the user's intention (`Test`, `VerifyPackage`, `RequireApproval`) over runner mechanics (`RunShell`, `SetEnvironmentVariable`) for common operations.
2. Keep an escape hatch for repository-specific work, but give it a stable name, declared result type, and explicit capability metadata.
3. Use the same concept names across providers; map provider-specific event and execution details in adapters.
4. Make control flow readable: triggers, conditions, dependencies, parallel groups, and failure policies should be visible when reviewing the recipe.
5. Do not conflate a step's result with the pipeline's policy verdict.
6. Do not conflate observation, evaluation, authorization, and execution.
7. Treat missing, skipped, cancelled, timed out, failed, and unknown as different states.
8. Keep secrets out of recipe text, reports, and model context. Refer to named secret/environment bindings without exposing values.
9. Make dry-run/plan inspection side-effect-free. Defining or printing a recipe must never invoke registered delegates.
10. Make the rendered recipe deterministic so changes are reviewable and suitable for audit.

## Design boundaries

CiAi's current implementation includes read-only GitHub Actions observation, normalized evidence contracts, and an initial definition-only pipeline/step model. The fluent vocabulary above is a discovery target, not a promise that all of these methods or execution features exist today.

The first implementation milestones should remain narrow:

1. Stabilize trigger, step identity, typed outcome, and deterministic plan semantics.
2. Model dependencies, conditions, skip/failure/cancellation outcomes, and validation.
3. Add provider-neutral evaluation that compares observed evidence with declared requirements.
4. Define explicit, candidate-bound authorization contracts.
5. Only then consider an opt-in executor or provider adapter, with publishing/deployment separately reviewed and disabled by default.

## Open design questions

- Should common operations such as `Build`, `Test`, and `PackNuGet` be semantic step kinds, reusable recipe helpers, or provider-adapter conventions?
- Should triggers start separate named recipes, or should one pipeline contain multiple trigger-scoped stages?
- How should matrix expansion, retries, and concurrency be represented in a deterministic definition?
- Which result vocabulary is shared across all steps, and which details belong in typed step-specific outputs?
- How should CiAi consume native provider protections without duplicating or weakening them?
- What minimum provenance is required before an artifact can be promoted or published?
