# CiAi Declarative Pipeline Builder — Design Proposal

**Status:** Phase 1 definition model, an initial Phase 2 in-process executor, and an initial required-evidence evaluator exist on `development`. Phase 2 is not complete: normalized result semantics and canonical deterministic reporting need more contract coverage; the GitHub Actions host adapter and publishing adapter are not implemented. The broader architecture remains in progress.  
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

## Composition-first builder model

The builder should hide mechanical detail without hiding policy. Prefer a small set of intention-level `.WithX()` capabilities that expand into ordinary, inspectable plan nodes. Do not turn every low-level operation into another method on the top-level builder.

Illustrative consumer recipe (target syntax, not a current API):

    var pipeline = CiAi.For("TrentBest/FSM_COS")
        .WhenPullRequest()
            .WithStandardValidation()
        .WhenDevelopment()
            .WithStandardValidation()
            .WithBenchmarks(options => options
                .ScaleTo(1, 10, 100, 1_000)
                .RetainRawReports())
            .WithPackageValidation()
        .WithNuGetRelease(options => options
            .RequireCompleteArtifacts()
            .RequireOwnerApproval())
        .Build();

### Composition rules

1. **Intent at the top; mechanics underneath.** A repository selects capabilities such as standard validation, benchmarks, package validation, or a NuGet release. Each capability expands into smaller plan nodes with stable IDs and visible dependencies.
2. **Parameters stay with the capability.** Use an options callback only when defaults are insufficient. Benchmark scale belongs inside `WithBenchmarks(options => ...)`; package artifact policy belongs inside `WithPackageValidation(...)`. Avoid a giant top-level options object that forces consumers to learn unrelated knobs.
3. **Capabilities are reusable values, not hidden side effects.** A capability contributes an inspectable definition of work, prerequisites, expected outputs/evidence, failure behavior, and policy requirements. Constructing or printing a recipe never runs work.
4. **Composition order and dependencies are explicit.** Preserve a deterministic expansion order, validate duplicate IDs and incompatible combinations, and represent dependencies as plan data rather than relying only on the order methods happen to be called.
5. **Extensions are a first-class escape hatch.** Repositories should be able to define their own `.WithFunStuff()` extension by composing existing capabilities and a small number of repository-specific operations. Customization should not require forking CiAi.
6. **Defaults cannot weaken safety.** A capability may declare that approval or evidence is required, but the name of a fluent method does not grant authorization. The runner must enforce a real host-provided decision bound to the candidate and plan.
7. **Inspect the expansion.** The short recipe and its expanded plan are two views of the same definition. Users should be able to inspect exactly which work, evidence, outputs, and gates a capability adds.

### Parameter and extension example

A repository-specific convenience method should compose standard capabilities rather than duplicate their internals:

    public static CiPipelineBuilder WithFunStuff(
        this CiPipelineBuilder pipeline,
        BenchmarkOptions options)
    {
        return pipeline
            .WithStandardValidation()
            .WithBenchmarks(configure => configure
                .ScaleTo(options.Sizes)
                .RetainRawReports())
            .WithPackageValidation();
    }

This example communicates the intended composition pattern; `WithStandardValidation`, `WithBenchmarks`, `WithPackageValidation`, and `BenchmarkOptions` are proposed names, not existing APIs. The first implementation should prove one capability end-to-end before multiplying the fluent surface.

### What the first prototype must prove

- A concise consumer recipe expands into a deterministic, inspectable plan.
- A capability can be composed into more than one recipe without copying its definition.
- Options reach only the capability they configure.
- Tests can inspect expansion without executing any registered delegate.
- Required evidence and privileged gates survive composition and cannot be removed by an innocent-looking convenience extension.
- The expanded plan explains each operation and why it is present, without requiring the user to read implementation code.

## Inter-repository dependency release gates

CiAi must treat a package dependency as an explicit release gate, not merely a project reference or a notification between repositories. This supports the development workflow in which a consumer uses an upstream repository's `development` branch while both projects are changing, then transitions to the exact NuGet package once the upstream release is published and verified.

A concrete acceptance case is the staged release work in `TrentBest/TheSingularityWorkshop.FSM_Serialization`: development may need to continue while upstream package work is in flight, but the intended consumer release must then be held at a durable gate until prerequisites are published. The consumer must restore, rebuild, and test against those exact feed packages before its own release can proceed. The blocked candidate must remain identifiable and resumable; it must not be treated as abandoned, silently retargeted to a moving branch, or published early. See [issue #16](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/16).

### Intended lifecycle

1. **Develop against source.** A consumer may reference the upstream `development` branch/project during coordinated development. The dependency is labeled as a development source; this state is not proof that a published package is usable.
2. **Validate upstream candidate.** Upstream build, tests, package validation, and declared evidence must pass for a specific commit and package identity.
3. **Authorize and publish upstream.** The owner/policy explicitly authorizes the upstream publication. A green build or observed workflow cannot grant that authorization.
4. **Verify publication.** Query NuGet to confirm the exact package ID/version is available. Record the source commit and available package provenance. A successful upload step alone is not sufficient.
5. **Transition the consumer.** Change the consumer from the development/project reference to the exact published package version. Keep the change auditable and reviewable; do not silently mutate a consumer's dependency file.
6. **Verify the consumer against the release.** Restore from the package feed, ensure the build actually consumes the published version (not a project reference or stale cache), then run the consumer's required build, tests, compatibility checks, and any declared benchmarks.
7. **Authorize and publish the consumer separately.** The consumer can become a release candidate only when its own required evidence passes. Its own publication requires its own explicit authorization and post-publication verification.
8. **Continue downstream.** A verified consumer publication may trigger the same lifecycle for its dependents. Every dependency edge remains an independent gate.

### Dependency plan contract

A dependency declaration should be short at the recipe level but expand into inspectable plan data, including:

- upstream repository and development source;
- published package ID and exact version-selection policy;
- expected source commit/provenance when available;
- transition method (reviewable commit, pull request, or repository-configured equivalent);
- required downstream evidence and verification steps;
- failure behavior and any owner authorization requirements.

The dependency graph must be deterministic and cycle-checked. Missing, stale, failed, indeterminate, or identity-mismatched evidence blocks the downstream transition/release. A branch update is not a publication event; a publication event is not proof of downstream compatibility; downstream compatibility is not permission to publish.

### Target consumer syntax

The following is illustrative target syntax, not an implemented API:

```csharp
var pipeline = CiAi.For("TrentBest/FSM_COS")
    .WithDependency("TrentBest/FSM_API", dependency => dependency
        .DevelopAgainstBranch("development")
        .ReleaseAsNuGet("TheSingularityWorkshop.FSM_API")
        .RequirePublishedVersion())
    .WithStandardValidation()
    .WithPackageValidation()
    .WithNuGetRelease(release => release
        .RequireAllDependenciesVerified()
        .RequireOwnerApproval())
    .Build();
```

This recipe should expand to concrete gates for upstream publication evidence, exact package-feed verification, consumer dependency transition, downstream restore/build/test, and the consumer's separate release authorization. The fluent names above are proposals; do not document them as available until implemented and covered by contract tests.

### Safety boundary

An upstream release can trigger downstream verification automatically, but must never silently publish the downstream package. Each package publication remains separately authorized. If downstream verification fails, the downstream release stays blocked and the report identifies the failed evidence; the system must not merge or publish an unverified transition as a workaround.

Track implementation in [issue #16 — Model inter-repository dependencies as verified release gates](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/16).

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

Still outstanding in Phase 1: trigger-scoped fluent recipe ergonomics, dependency/condition models and validation, normalized machine-readable output, and broader edge-case coverage. The initial slice and the job-pagination regression correction are committed on `development`. The [CiAi Contract Verification run](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/40460505134) and [Cog Ecosystem Integration run](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/40460505108) passed on the implementation/test commit; subsequent changes to release-readiness documentation were not part of that tested commit.

- Test that definition and rendering never invoke delegates or access secrets.

### Phase 2 — deterministic in-process execution for safe steps

- Initial implementation exists in `CiPipelineExecutor` on `development`: sequential execution, a step context, cancellation checks, bounded exception diagnostics, and explicit succeeded/failed/skipped/cancelled/blocked outcomes. The executor blocks privileged capabilities by default.
- Contract tests cover definition non-execution, step order, false results, exceptions, skipped later work, and privileged-step blocking.
- Result semantics in this prototype are explicit for `bool` and `CiStepResult`: `false` and `CiStepResult.Failure(...)` fail the step; `CiStepResult.Success()` succeeds; exceptions fail it. Other returned values are retained as step outputs, not interpreted as status codes. A non-boolean value (including an integer exit code) is **not** interpreted as success/failure. Do not use this executor to wrap commands whose failure is encoded in a non-boolean result unless the adapter maps that status to `CiStepResult`.
- Remaining Phase 2 work: extend normalized result semantics to skipped/indeterminate outcomes, test cancellation and diagnostic bounds, define canonical report rendering that excludes wall-clock timestamps, and test task/result edge cases.
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
