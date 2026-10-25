# CI Workflow Domain Catalog

**Status:** Product-discovery inventory; intentionally broader than the currently implemented API.  
**Ordering:** Most operationally complex and failure-prone first, progressing toward simple, focused checks.  
**Scope:** Common continuous-integration, continuous-delivery, release-engineering, and repository-automation workflows. This is a structured working taxonomy, not a statistical ranking or a claim that every team uses every workflow.

## Why this document exists

CiAi should be designed from the workflows teams actually need to express and govern—not from a list of YAML keywords. The product opportunity is to make complicated delivery systems easier to understand, compose, validate, audit, and safely operate.

The core product question for each workflow family is:

> Can a developer state the desired policy in fluent, provider-neutral language, and can CiAi explain what it means, determine what evidence is missing, and keep consequential actions behind explicit authorization?

The catalog is ordered roughly from **hardest/highest consequence** to **simplest/narrowest**. Complexity reflects dependencies, state, security consequences, provenance requirements, and failure ambiguity—not just the number of steps.

## A common model for every workflow

Each family should eventually be expressible through the same conceptual layers:

1. **Intent / recipe** — what outcome the team wants.
2. **Triggers** — which event, revision, schedule, or manual request makes it eligible.
3. **Plan** — steps, dependencies, conditions, matrix dimensions, concurrency, and failure policy.
4. **Execution observations** — what the CI provider says actually happened.
5. **Evidence** — logs, test/coverage reports, security findings, artifacts, digests, provenance, approvals, and source identity.
6. **Evaluation** — compare the evidence with declared requirements; return pass, fail, or indeterminate with explanations.
7. **Authorization** — an explicit trusted decision for actions with meaningful external consequences.
8. **Execution / promotion** — perform only the authorized action and verify its outcome.
9. **Audit report** — preserve recipe identity, source commit, run/attempt identity, decisions, evidence gaps, and resulting artifact/deployment identity.

**Non-negotiable distinctions:** observed is not evaluated; evaluated is not authorized; authorized is not executed; requested is not completed. Missing, skipped, cancelled, timed out, failed, and unknown are different outcomes. A green run for another commit is not evidence for this commit.

---

## Tier 1 — Complex, cross-stage, high-consequence workflows

### 1. Governed release orchestration across environments

**User goal:** Take a source revision through validation, candidate creation, approval, publication, deployment, and post-release verification without losing identity between stages.

**Typical flow:** source/tag validation → build/test/security checks → immutable candidate → artifact inspection → policy evaluation → explicit approval → publish/promote → deploy → smoke/health checks → retain evidence or initiate a controlled rollback.

**Why it is hard**
- Multiple systems, environments, credentials, and trust boundaries.
- A later stage may accidentally rebuild different bits rather than promote the tested artifact.
- Approval can become stale or apply to the wrong commit, version, digest, destination, or run.
- Retries can duplicate non-idempotent publication or deployment actions.
- “Workflow succeeded” may hide a skipped verification or incomplete evidence.

**Evidence / invariants**
- Exact repository and full source SHA; trigger and run/attempt identity.
- Package/image/artifact ID, version, digest, and provenance.
- Required check inventory, including explicit missing/unknown states.
- Approval identity, scope, time, policy revision, and candidate binding.
- Target environment/feed, deployment result, and post-deployment health evidence.

**Candidate fluent vocabulary:** `Release`, `BuildCandidate`, `RequireChecks`, `VerifyProvenance`, `EvaluatePolicy`, `RequireApprovalFor`, `PromoteExactArtifact`, `DeployTo`, `VerifyDeployment`, `RollbackIf`.

**Product boundary:** Defining a recipe or passing checks must never authorize publishing/deployment. Provider-native protection and an explicit trusted authorization mechanism remain authoritative.

### 2. Multi-repository / monorepo release trains and dependency graphs

**User goal:** Coordinate compatible changes across libraries, services, generated clients, schemas, and downstream consumers.

**Typical flow:** detect affected components → calculate dependency closure → validate compatible versions → build/test in dependency order or parallel → create a coordinated candidate set → verify cross-component compatibility → release in a safe order.

**Why it is hard**
- Dependency graphs change over time and may contain cycles or generated edges.
- A changed shared component can affect distant consumers.
- Independent jobs can finish with incompatible versions or mixed source revisions.
- Partial failure can leave the ecosystem in a mixed release state.

**Evidence / invariants**
- Component graph and graph revision; affected-component rationale.
- Exact source revisions and resolved dependency versions per component.
- Compatibility/test results for required edges, not just nodes.
- Release-set identity and partial-completion state.

**Candidate vocabulary:** `DiscoverAffected`, `UseDependencyGraph`, `BuildInDependencyOrder`, `ValidateCompatibility`, `CoordinateRelease`, `RequireConsistentRevisions`.

### 3. Artifact provenance, supply-chain integrity, and attestations

**User goal:** Prove that the exact binary being promoted came from the intended source and trusted build process.

**Typical flow:** build → enumerate outputs → inspect archives/metadata → compute digests → bind artifact to repository/commit/workflow/toolchain → verify provenance/signature/attestation → retain immutable evidence.

**Why it is hard**
- A successful build does not prove that all outputs were retained.
- Filenames alone do not prove bytes, contents, source, or provenance.
- Symbols, SBOMs, attestations, signatures, and platform-specific outputs can be omitted.
- Artifact storage can expire, be overwritten, or be detached from run metadata.

**Evidence / invariants**
- Inventory of expected versus observed outputs.
- Archive integrity and actual content checks.
- Digests and, where applicable, signatures/attestations.
- Package ID/version, PDB or debug-symbol presence, SBOM, license metadata.
- Source SHA, workflow/run/attempt, builder identity, and artifact-store identity.

**Candidate vocabulary:** `ExpectArtifacts`, `VerifyInventory`, `InspectArchive`, `VerifyDigest`, `VerifySymbols`, `RequireAttestation`, `BindToSource`, `RetainEvidence`.

**Important distinction:** CiAi's initial NuGet inventory verifier checks caller-supplied relative paths for expected `.nupkg` and `.snupkg` names. It does not itself download artifacts, inspect archive bytes, or verify provenance.

### 4. Security-gated CI/CD and policy enforcement

**User goal:** Run security analysis and ensure findings or policy violations block the right operations.

**Typical flow:** static analysis, dependency and secret scanning, license checks, container/IaC scanning, policy evaluation → triage/suppression review → release gate.

**Why it is hard**
- Findings have severity, confidence, age, reachability, ownership, and suppression context.
- Tools use different schemas and severities.
- A scan can be skipped, stale, misconfigured, or incomplete while the overall workflow appears green.
- Policy exceptions need an owner, scope, rationale, expiry, and audit trail.

**Evidence / invariants**
- Scanner/tool version, scope, source SHA, completion status, and report.
- Normalized finding identities and severity mapping.
- Explicit thresholds and exception records; stale reports are not current proof.
- Separate “scan completed” from “policy passed.”

**Candidate vocabulary:** `ScanDependencies`, `ScanSecrets`, `ScanCode`, `CheckLicenses`, `EvaluateSecurityPolicy`, `AllowExceptionUntil`, `RequireFreshEvidence`.

### 5. Deployment strategies, migrations, and rollback safety

**User goal:** Deliver changes gradually and detect unsafe outcomes before broad rollout.

**Typical flow:** validate candidate → deploy to canary/staging → smoke tests and health metrics → progressive rollout → migration compatibility checks → final verification or rollback.

**Why it is hard**
- Deployment state persists outside the CI run.
- Database changes may be irreversible or require expand/migrate/contract sequencing.
- A command's exit code may not mean the service is healthy.
- Rollback may be unsafe after schema or data changes.

**Evidence / invariants**
- Exact artifact digest and target environment.
- Deployment controller result plus independent health evidence.
- Migration version/state and backward-compatibility requirements.
- Rollback plan, preconditions, and explicit safe/unsafe status.

**Candidate vocabulary:** `DeployCanary`, `WaitForHealth`, `PromoteTraffic`, `RequireMigrationCompatibility`, `RollbackIf`, `VerifyEnvironmentState`.

### 6. Cross-platform and multi-target compatibility matrices

**User goal:** Verify supported framework/runtime/OS/architecture combinations without accidentally omitting a required combination.

**Typical flow:** expand matrix → restore/build/test per leg → collect results and coverage → aggregate required legs.

**Why it is hard**
- Matrix dimensions can multiply rapidly.
- Conditional exclusions and include/override rules are easy to misunderstand.
- Missing or cancelled legs must not silently count as success.
- Some combinations are intentionally unsupported and need documented exclusions.

**Evidence / invariants**
- Declared matrix and the actual expanded set.
- Result for every required combination.
- Explicit exclusions, reasons, and policy version.
- Aggregation that distinguishes failed, missing, skipped, cancelled, and unknown.

**Candidate vocabulary:** `Matrix`, `OnFrameworks`, `OnOperatingSystems`, `OnArchitectures`, `ExcludeCombination`, `RequireEveryLeg`, `AggregateResults`.

### 7. Generated code, schemas, documentation, and reproducibility

**User goal:** Regenerate checked-in or packaged outputs and prove they match the authoritative source.

**Typical flow:** generate from schema/source → compare outputs with repository → validate links/structure → fail if generated files are stale → optionally publish generated docs.

**Why it is hard**
- Tool versions, locale, timestamps, ordering, and environment can cause nondeterministic diffs.
- Generated output may be mistaken for source of truth.
- Some generation is lossy or requires network inputs.
- “No diff” is meaningful only if generation actually ran successfully.

**Evidence / invariants**
- Generator/version/configuration and input identities.
- Successful generation plus deterministic comparison.
- Clear source-of-truth rules and documented update command.
- Explicit handling of external data and timestamps.

**Candidate vocabulary:** `Generate`, `CheckGeneratedFiles`, `RequireReproducibleOutput`, `ValidateDocumentation`, `FailIfStale`.

### 8. Dynamic test selection, change impact, and quality gates

**User goal:** Run the smallest safe test set for a change while preserving confidence and coverage requirements.

**Typical flow:** inspect diff/dependency graph → choose targeted tests → run fast checks → expand to integration/full suite according to risk → aggregate coverage and test results.

**Why it is hard**
- Test-selection mistakes can miss regressions.
- Test history can be stale or statistically misleading.
- Coverage thresholds can be gamed by changed denominator or omitted files.
- Flaky tests and quarantines can mask real failures.

**Evidence / invariants**
- Selection rationale and mapping from changed components to tests.
- Required baseline suite and fallback when selection is uncertain.
- Test results, retries, flake classification, and coverage scope.
- Threshold policy tied to the correct source revision.

**Candidate vocabulary:** `SelectTestsForChanges`, `RunFastChecks`, `ExpandIfRisky`, `RequireCoverage`, `QuarantineWithExpiry`, `ExplainTestSelection`.

---

## Tier 2 — Common engineering workflows with meaningful orchestration

### 9. Build once, test once, promote the same artifact

**User goal:** Avoid rebuilding different bits for test, staging, and production.

**Hard parts:** artifact retention, digest identity, environment-specific configuration, promotion permissions, and evidence association.

**Candidate vocabulary:** `BuildOnce`, `TestArtifact`, `PromoteExactArtifact`, `VerifyDigest`.

### 10. Release candidate and package creation

**User goal:** Create a versioned package/container/binary candidate and verify its contents before any publication.

**Hard parts:** version derivation, tag/commit correlation, metadata, licenses/readme, symbols, platform variants, duplicate version detection, and package size/content checks.

**Candidate vocabulary:** `OnTag`, `PackNuGet`, `BuildContainer`, `VerifyPackageMetadata`, `RequireFiles`, `CreateCandidateReport`.

### 11. Dependency updates and automated maintenance PRs

**User goal:** Update dependencies or generated lockfiles, validate compatibility, and propose a reviewable change.

**Hard parts:** update grouping, lockfile consistency, vulnerability context, breaking changes, rate limits, generated PR loops, and safe automation permissions.

**Candidate vocabulary:** `CheckDependencyUpdates`, `UpdateLockfiles`, `ValidateUpgrade`, `OpenReviewRequest`, `RequireHumanReviewForBreakingChanges`.

### 12. Cache-aware builds and dependency restoration

**User goal:** Reduce CI time without letting stale or poisoned caches affect correctness.

**Hard parts:** cache-key completeness, lockfile/toolchain invalidation, cross-branch trust, cache size/eviction, and treating cache as an optimization rather than source of truth.

**Candidate vocabulary:** `CacheBy`, `RestoreDependencies`, `InvalidateWhenChanged`, `BuildWithoutCacheIfNeeded`.

### 13. Flaky tests, retries, timeouts, and cancellation

**User goal:** Make transient failures visible and keep obsolete or hung work from wasting resources.

**Hard parts:** distinguishing flakiness from product failures, bounded retry, timeout scope, cancellation propagation, and avoiding repeated non-idempotent operations.

**Candidate vocabulary:** `TimeoutAfter`, `RetryAtMost`, `RetryOnlyIfTransient`, `CancelSupersededRuns`, `ReportFlakiness`.

### 14. Parallel jobs, dependency graphs, and fan-in

**User goal:** Run independent checks concurrently, then require the right results before proceeding.

**Hard parts:** dependency cycles, concurrency limits, skipped upstream jobs, partial failure, and confusing a join with a blanket “all green” requirement.

**Candidate vocabulary:** `InParallel`, `After`, `DependsOn`, `Join`, `RequireAll`, `RequireAny`, `MaxConcurrency`.

### 15. Scheduled, manual, and event-driven automation

**User goal:** Run nightly suites, maintenance tasks, release jobs, or manually dispatched workflows with explicit parameters.

**Hard parts:** timezone/schedule semantics, duplicate runs, permissions, parameter validation, and manual triggers bypassing assumptions made for pull requests.

**Candidate vocabulary:** `OnSchedule`, `OnManualDispatch`, `WithParameters`, `ValidateInputs`, `ConcurrencyGroup`.

### 16. CI status reporting, diagnosis, and evidence collection

**User goal:** Explain what ran, what did not run, why a job failed, and which source/artifact the result applies to.

**Hard parts:** provider-specific status models, retries/attempts, incomplete pagination, missing jobs, stale evidence, and noisy logs.

**Candidate vocabulary:** `Observe`, `CollectEvidence`, `ExplainFailure`, `CompareAttempts`, `ReportMissingChecks`, `WriteMarkdownReport`.

**CiAi relevance:** This is a foundational capability, but observation alone is not a policy verdict. Exact repository and full commit SHA correlation must be preserved.

---

## Tier 3 — Focused, simpler workflows

### 17. Pull-request validation

**User goal:** On a pull request, restore, build, test, lint, and report without publishing.

**Candidate vocabulary:** `OnPullRequest`, `Restore`, `Build`, `Test`, `Lint`, `Report`.

**Core guard:** no privileged side effect is implied by a validation recipe.

### 18. Main-branch integration validation

**User goal:** Repeat required checks against the exact committed revision after changes reach the main branch.

**Candidate vocabulary:** `OnPushToMain`, `RequireChecks`, `RecordCommit`.

### 19. Basic static checks and formatting

**User goal:** Catch formatting, analyzer, lint, type, and compile errors early.

**Candidate vocabulary:** `FormatCheck`, `Lint`, `Analyze`, `TypeCheck`, `Build`.

### 20. Unit tests and simple coverage thresholds

**User goal:** Run a focused suite and enforce a clearly defined minimum coverage policy.

**Candidate vocabulary:** `Test`, `CollectCoverage`, `RequireCoverageAtLeast`, `PublishTestResults`.

**Core guard:** define coverage scope, exclusions, and whether the threshold is total or changed-code coverage.

### 21. Documentation and repository hygiene checks

**User goal:** Validate links, required files, license/readme metadata, generated docs, and repository conventions.

**Candidate vocabulary:** `ValidateReadme`, `CheckLinks`, `RequireLicense`, `CheckRepositoryPolicy`.

### 22. Minimal build/package smoke check

**User goal:** Prove a project restores, builds, and produces its expected output.

**Candidate vocabulary:** `Restore`, `Build`, `Pack`, `RequireOutput`.

**Core guard:** successful process exit is necessary but may not be sufficient; verify expected outputs and metadata.

---

## Cross-cutting capabilities (not separate workflow families)

These features recur across almost every tier and should have consistent semantics.

- **Triggers:** pull request, push, tag, schedule, manual dispatch, release event, upstream workflow completion.
- **Control flow:** dependencies, conditions, fan-out/fan-in, matrix expansion, concurrency, timeout, cancellation.
- **Outcome vocabulary:** succeeded, failed, skipped, cancelled, timed out, incomplete, unknown; preserve raw provider values too.
- **Evidence model:** source identity, workflow/run/attempt/job/step, timestamps, links, logs/reports, artifact inventory and digest.
- **Policy evaluation:** required checks, freshness, severity thresholds, allowed exceptions, matrix completeness, artifact/provenance rules.
- **Security:** least privilege, named secret references, environment boundaries, immutable action/tool versions, no secret leakage in logs/reports.
- **Authorization:** explicit authority, exact action/candidate/target binding, expiry, replay resistance, audit trail.
- **Idempotency and recovery:** bounded retries, deduplication, cancellation, compensating actions, rollback safety.
- **Observability:** readable plan, deterministic rendering, useful failure explanations, machine-readable evidence and human-readable report.
- **Portability:** provider-neutral intent with adapters for provider-specific execution and semantics; never erase meaningful provider differences.
- **Escape hatches:** custom operations declare inputs, outputs, capabilities, side effects, and failure semantics.

## How to prioritize the product

Do not implement the catalog top-to-bottom as a giant feature list. Prioritize the shared abstractions that let the complex families become composable and trustworthy.

### Suggested implementation sequence

1. **Recipe model and deterministic plan:** stable identity, trigger declarations, typed steps, explicit dependencies/conditions, validation, readable rendering; inspection must be side-effect-free.
2. **Truthful outcomes and evidence:** normalize provider facts while retaining raw values; represent missing/incomplete/unknown distinctly; bind every observation to repository + full SHA + run/attempt.
3. **Policy evaluation:** express required checks and artifact requirements; produce a reasoned pass/fail/indeterminate report, never an authorization.
4. **Artifact verification:** inventory → byte/archive integrity → package content/metadata → symbols/SBOM/attestation → provenance and source binding.
5. **Governed authorization contracts:** explicit trusted authority, candidate-bound approvals, expiry/replay checks, target/action scope; no implicit approval by passing tests.
6. **Execution safety primitives:** bounded concurrency, timeout/cancellation, idempotency, retry classification, dry-run/plan, audit trail.
7. **Provider adapters:** start with read-only GitHub Actions evidence, then add capabilities only when their semantics and permissions are clearly specified.
8. **Opt-in consequential adapters:** publication/deployment only after independent review, explicit owner approval, and dedicated end-to-end tests. Keep these outside the core recipe-definition contract.

### First valuable product wedge

A practical first wedge is **“explain and verify a CI/release workflow before it surprises you.”** CiAi can progressively:
- describe the intended pipeline in fluent terms;
- show the plan and dependency graph;
- compare required checks with observed provider evidence;
- identify missing jobs, stale/mismatched runs, and incomplete artifact inventories;
- explain what evidence supports each conclusion;
- produce a candidate report without publishing or deploying.

This creates value before CiAi executes anything. It also gives the later execution layer a trustworthy foundation.

## Research plan: turn the catalog into an evidence-backed product map

This is an initial domain hypothesis, not a measured market-share report. To validate it, gather representative workflows and incidents from:
- CiAi's own workflows and cross-Cog integration;
- FSM_API/FSM_COS and package-release workflows;
- a small sample of real-world public workflow definitions from different languages and deployment styles;
- documented CI failure modes: missing artifacts, wrong-SHA evidence, incomplete matrices, skipped checks, stale security results, non-idempotent retries, and unauthorized releases.

For each example, record source URL/revision, workflow family, intended outcome, actual control flow, hidden assumptions, failure mode, evidence available, security consequence, and what a fluent recipe would look like. Avoid treating one repository or one provider's syntax as representative of the whole market.

## Questions to resolve

1. Which workflow families occur most often in the intended first customer segment: open-source libraries, internal services, multi-package ecosystems, or deployment-heavy teams?
2. Is the strongest initial paid value authoring recipes, explaining existing pipelines, verifying release evidence, or governing execution?
3. Which concepts are universal enough for core vocabulary, and which should remain adapter-specific?
4. What is the minimum useful execution capability, if any, before read-only diagnosis and policy evaluation are compelling?
5. How should recipes be versioned, reviewed, tested, and compared when policy changes?
6. What evidence and authorization model is necessary before a paid product can safely influence production releases?

## Implementation honesty

The names and examples in this document are **proposed vocabulary**, not a statement that the corresponding methods exist. CiAi currently has read-only GitHub Actions observation and evidence/reporting components, a definition-only pipeline-builder slice, and an initial local NuGet artifact-inventory verifier. It does not yet provide a complete fluent CI DSL, general workflow executor, full automated archive/provenance inspection, or a publishing/deployment engine.

The release readiness tracker remains the authority for verified implementation status. No publication or deployment is implied by this catalog.
