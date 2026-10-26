# CiAi Workflow Domain Map

**Trent Best · The Singularity Workshop**

CiAi exposes CI to AI through typed, inspectable facts and bounded capabilities. This catalog defines the initial workflow-domain vocabulary; it does not claim that every listed workflow is already executable.

## Workflow families

| Stable ID | Domain | Purpose | Evidence vocabulary |
|---|---|---|---|
| `build-test` | Build and test | Restore, compile, test, retain results | source, build, test |
| `code-quality` | Code quality | Lint, formatting, static quality checks | source, quality-report |
| `security` | Security analysis | Dependency, secret, vulnerability, static security checks | source, security-report |
| `compatibility` | Compatibility matrix | Supported runtimes, frameworks, and platforms | source, matrix-completeness |
| `documentation` | Documentation | Links, examples, generated docs, branding, support material | source, documentation-report |
| `package` | Package creation | Build outputs and verify package inventory | source, package-inventory |
| `release-candidate` | Release candidate | Bind package/version/revision/digest into a reviewable candidate | candidate, package-digest, source-provenance |
| `publication` | Publication | Publish only after explicit candidate-bound authorization | candidate, authorization, target |
| `deployment` | Deployment | Promote a verified candidate to a named environment | candidate, authorization, environment |
| `deployment-validation` | Post-deployment validation | Verify health and smoke tests after promotion | deployment, health, smoke-test |
| `performance` | Performance and benchmarks | Named runs, baseline comparisons, regression evidence | source, benchmark-result, baseline |
| `integration` | Integration and contract tests | Component and API boundaries | source, contract-report |
| `artifact-handoff` | Artifact handoff | Inventory, transfer, verify, acknowledge, correlate | artifact, digest, receipt |
| `provenance` | Supply-chain provenance | Bind output to source, build, tools, and attestations | artifact, source-provenance, builder-identity |
| `scheduled-maintenance` | Scheduled maintenance | Recurring checks and freshness policy | schedule, timestamp, freshness |
| `concurrency` | Concurrency and cancellation | Duplicate work, bounded parallelism, supersession | execution, concurrency-key, outcome |
| `recovery` | Retries and recovery | Retry classification, replay, rollback, compensation | execution, retry-policy, idempotency-key |
| `multi-repository` | Multi-repository orchestration | Exact upstream repository/revision correlation | repositories, dependency-graph, source-provenance |
| `matrix` | Matrix orchestration | Bounded variants and matrix completeness | matrix, matrix-completeness |
| `observability` | Run diagnosis and reporting | Explain failures, skips, cancellation, timeouts, unknowns | run, evidence, diagnostic |
| `repository-hygiene` | Repository hygiene | Required files, metadata, branch and repository policy | repository, policy-report |
| `smoke` | Minimal build/package smoke check | Restore/build/output existence and metadata | source, build, output-inventory |

## Cross-cutting contracts

These concerns apply across families and should not be reimplemented inconsistently in each one:

- **Triggers and control flow:** triggers, dependencies, conditions, fan-out/fan-in, bounded matrix expansion, concurrency, timeout, cancellation.
- **Truthful outcomes:** succeeded, failed, skipped, cancelled, timed out, incomplete, blocked, and unknown remain distinguishable.
- **Evidence provenance:** bind facts to repository, full commit SHA, workflow/run/attempt/job/step, timestamps, evidence URLs, and artifact identity when available.
- **Policy evaluation:** compare declared requirements with observed evidence; missing, ambiguous, stale, or mismatched evidence never silently passes.
- **Security:** least privilege, named secret references, immutable tool/action versions, bounded outputs, no secret values in reports.
- **Authorization:** explicit authority bound to exact action, candidate, target, plan, expiry, and replay controls. Test success is not authorization.
- **Idempotency and recovery:** bounded retries, stable deduplication keys, cancellation, compensation, and safe rollback semantics.
- **Observability and portability:** deterministic plans, machine-readable facts, readable explanations, provider adapters that preserve meaningful provider differences.
- **Custom operations:** declare inputs, outputs, capabilities, side effects, and failure semantics.

## Validated workflow plans

`CiWorkflowPlanValidator` validates data-only plan drafts before a host considers execution. It checks stable plan/step IDs, registered workflow domains, dependency references, duplicate/self-dependencies, invalid conditions, and dependency cycles. Valid plans receive a deterministic topological order; invalid plans have no executable order and cannot be rendered as a recipe. Rendering is side-effect-free and includes the declared condition and dependencies.

The current condition vocabulary is descriptive (`AllDependenciesSucceeded`, `Always`, `AnyDependencyFailed`, and `AllDependenciesCompleted`). The validator does not execute steps or implement runtime condition evaluation. A host must still map provider observations to authoritative terminal outcomes before acting on those conditions.

## Implemented foundation

The code catalog in `CiWorkflowDomainCatalog` exposes stable family IDs, descriptions, and evidence labels. `CiWorkflowDomainEvaluator` validates that a requested evidence policy uses vocabulary declared by its domain, then delegates to the existing fail-closed evaluator that binds evidence to the exact repository and full SHA.

This layer evaluates supplied facts only. It does not fetch evidence, prove a remote provider's authority, invoke an LLM, execute a pipeline, or authorize publishing/deployment. Catalog entries are scaffolding and vocabulary, not a claim that every family has a complete adapter.

## Recommended implementation order

1. **Plan model:** stable step identity, dependencies, conditions, validation, deterministic rendering, side-effect-free inspection.
2. **Outcome and evidence model:** provider-neutral status with raw provider facts retained; exact repository/SHA/run/attempt binding.
3. **Policy evaluation:** family profiles, required checks, freshness, severity, artifact requirements, and explicit indeterminate verdicts.
4. **Artifact verification:** archives, package contents/metadata, symbols, SBOM/attestation, source provenance.
5. **Authorization contracts:** trusted authority and candidate/action/target-bound approval with expiry and replay protection.
6. **Execution safety:** timeout, cancellation, bounded concurrency, idempotency, retry classification, dry-run, audit trail.
7. **Provider adapters:** extend from read-only GitHub Actions evidence only where permission and semantic contracts are explicit.
8. **Consequential adapters:** publication and deployment remain separately gated, opt-in, and disabled by default until independently reviewed and tested.

The first product value is to explain and verify a workflow before it surprises its owner—not to turn model-generated text directly into privileged execution.

![The Singularity Workshop](../Avatar.jpg)
