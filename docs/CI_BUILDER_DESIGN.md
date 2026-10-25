# CiAi Declarative Pipeline Builder — Design Proposal

**Status:** Phase 1 definition-model slice committed on `development`; no executor, evidence evaluator, or publishing adapter is implemented. The broader architecture remains a proposal.  
**Purpose:** Make CI definitions readable as repository-owned recipes, while keeping execution, provider evidence, and release authorization distinct.

## Design goal

A repository should be able to express its CI policy in a compact, reviewable, strongly typed definition. A reviewer should be able to answer, by reading it:

1. Which events or branches activate each recipe?
2. Which steps run, and in what order?
3. What evidence or outputs does each step produce?
4. What happens when a step fails, is skipped, or returns an unknown result?
5. Which operations are privileged, and what explicit approval is required before they run?

The definition is the recipe. A runner interprets the recipe. A provider such as GitHub Actions remains authoritative for provider-run facts. CiAi's observer reports those facts; it does not infer owner intent or manufacture permission from a green workflow.

## Example target API

The syntax below is illustrative, not a current public API:

    var pipeline = CiAi.Create("TheSingularityWorkshop.CiAi")
        .OnPullRequest()
            .Restore()
            .Build()
            .Test()
            .VerifyPackage()
        .OnMainBranch()
            .Restore()
            .Build()
            .Test()
            .PackNuGet()
            .RequirePublishApproval()
            .PublishNuGet();

A repository-specific operation should also be possible:

    pipeline.Add<bool>("Publish NuGet", () => PublishToNuget());

The fluent example above remains the target API. The initial implemented slice is intentionally smaller: `new CiPipelineBuilder(repository).On(trigger).Add<T>(...).Build()`. It provides typed step metadata and deterministic recipe text, but not the polished trigger-scoped chaining API yet. The key is that the recipe reads like the work the repository intends to perform, not like a list of incidental runner commands.

## User workflows and vocabulary

Before expanding the API, see [CI User Workflows and Fluent Vocabulary](CI_USER_WORKFLOWS.md). It starts from the actual jobs developers ask CI to perform and separates triggers, work, control flow, evidence, policy evaluation, authorization, and execution. Its examples are target syntax, not claims about implemented methods.

## Core concepts

- **Pipeline definition:** immutable, inspectable description of triggers, ordered steps, dependencies, conditions, outputs, and policy requirements. Defining a pipeline must not execute it.
- **Trigger:** event/context selector such as pull request, push to main, tag, manual dispatch, or scheduled run. Trigger matching is distinct from step success.
- **Step:** named operation with declared input/output types, stable identity, and explicit failure/skip behavior.
- **Condition:** determines whether a step is eligible to run (for example, branch or prior result). A condition is not authorization.
- **Evidence:** provider facts or step outputs with source identity, commit/run correlation, status, and provenance. Evidence can be missing or incomplete; it must not silently become success.
- **Policy gate:** an explicit constraint for privileged actions, such as publishing a package. It records what approval is required and how the runner obtains it.
- **Executor/adapter:** host-controlled mechanism that performs a step. GitHub Actions can remain the initial execution authority; a local runner or other provider adapter is a separate decision.
- **Run report:** a deterministic account of the definition used, steps attempted, outcomes, evidence, skipped work, failures, and unresolved gates.

## Add<T> contract

A custom step such as Add<bool>("Publish NuGet", () => PublishToNuget()) is expressive, but a boolean return value is only a step result. It must not itself grant authority.

Recommended semantics:

1. The step has a stable ID and human-readable name.
2. The delegate is not invoked when the recipe is constructed or inspected.
3. The runner invokes it only when the step is selected for execution and all declared policy gates are satisfied.
4. true means the operation reported success; false means it reported an unsuccessful result. Exceptions are captured as failures with diagnostics. Cancellation is distinct from failure.
5. A publishing step must declare a privileged capability (for example, PublishPackage) and require an explicit host-supplied authorization decision before invocation.
6. The approval decision is bound to the intended repository, package ID/version, source commit, and run/plan identity. Approval for one candidate must not authorize a different candidate.
7. The runner must not treat an LLM response, a green Observer report, a branch condition, or a prior boolean result as owner authorization.
8. The definition and report should be inspectable without executing delegates or loading secrets.

A richer result type may ultimately be safer than a bare bool, because it can distinguish success, failure, skipped, cancelled, and indeterminate. The simple boolean overload can remain ergonomic while mapping into a richer internal result.

## NuGet publishing recipe

Publishing should be composed from ordinary, visible steps, with the privileged boundary unmistakable:

1. Restore dependencies.
2. Build the selected configuration.
3. Run tests and required static/security checks.
4. Pack the exact source commit.
5. Inspect the produced package: ID, version, target frameworks, dependencies, license, README, symbols, and repository provenance.
6. Verify that the package ID/version is intended and that the version is not already published.
7. Present the candidate identity and evidence to the authorized owner or configured approval provider.
8. Receive an explicit approval decision tied to that exact candidate.
9. Publish through a separately configured publishing adapter using least-privilege credentials.
10. Verify publication by querying the package feed and record the resulting package identity and provenance.

Failure, missing evidence, unknown status, or a rejected/expired approval must stop the privileged step. The report should make the reason visible. No credentials belong in the recipe, source tree, or model context.

**Important:** CiAi should not silently take over a repository's existing publish workflow. The first release-enabling integration should compose with the existing provider workflow and its environment/manual-approval protections. A native publishing executor is a later, separately reviewed capability.

## Relationship to Observer and release evidence

Keep four questions separate:

- **Observed:** What did the provider report for this exact repository, commit, run, attempt, job, and step?
- **Evaluated:** Does that evidence satisfy the recipe's declared required checks?
- **Authorized:** Did the configured owner/policy explicitly approve this exact privileged action?
- **Executed and verified:** Did the publishing adapter run, and can the package feed confirm the intended result?

A green observation is evidence for evaluation, not authorization. A passing evaluation is not authorization either. Authorization permits a specific action but does not prove that action succeeded. Publication success requires post-action verification.

## Recommended implementation sequence

### Phase 1 — declarative model, no execution

Initial slice committed on `development`:

- Immutable pipeline definition, trigger list, typed step result metadata, capability marker, and explicit approval-required flag.
- Builder with `On(...)`, generic `Add<T>(...)`, duplicate-ID rejection, and a single-use `Build()` boundary.
- Deterministic human-readable recipe rendering.
- Fail-closed builder validation for privileged capabilities without an approval requirement.
- Contract tests for delegate non-invocation, stable rendering and order, duplicate IDs, visible approval metadata, and builder lifecycle.

Still outstanding in Phase 1: trigger-scoped fluent recipe ergonomics, dependency/condition models and validation, normalized machine-readable output, and broader edge-case coverage. The initial slice has been committed remotely but has not yet been confirmed by a successful CI run.

- Test that definition and rendering never invoke delegates or access secrets.

### Phase 2 — deterministic in-process execution for safe steps

- Define an executor interface and typed step context/results.
- Implement sequential execution with cancellation, bounded diagnostics, deterministic ordering, and explicit skip/failure states.
- Add custom Add<T> steps and tests for true, false, exception, cancellation, and skipped execution.
- Keep provider-specific operations behind adapters. Do not add publish capability in this phase.

### Phase 3 — evidence evaluation

- Map observed provider runs to declared recipes and required check identities.
- Report missing, stale, truncated, failed, skipped, and unknown evidence distinctly.
- Bind evidence to exact repository and full commit SHA; retain run attempt and workflow identity.
- Do not equate matching display names alone with trusted required checks.

### Phase 4 — approval protocol and package publishing adapter

- Define a host-provided approval interface and auditable approval record.
- Bind approvals to candidate repository, source SHA, package ID/version, package digest, and intended feed.
- Add dry-run/plan output and explicit confirmation before any publishing call.
- Implement a narrowly scoped NuGet adapter only after policy, credential, replay, failure-recovery, and post-publication verification tests pass.
- Keep publication disabled by default and require explicit owner enablement. No auto-publish based only on a green build or model-generated recommendation.

## Acceptance criteria

- Reading the recipe is sufficient to understand what the repository intends to do.
- Recipe construction and inspection have no side effects.
- The same definition produces stable normalized output.
- Every step has explicit identity and outcome semantics.
- Missing/unknown evidence fails closed for release-critical checks.
- Privileged steps cannot execute without an explicit, candidate-bound approval decision.
- Tests prove that a passing observer report and a successful boolean step do not bypass approval.
- Publishing is not part of the read-only alpha; it remains a future opt-in capability.
