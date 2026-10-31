# The Singularity Workshop CiAi

**CiAi exposes CI to AI. It is not an AI-powered CI runner.** The lowercase `i` is intentional: CiAi supplies bounded capabilities and verified facts that an LLM can use.

CiAi is the application-owned boundary between continuous-integration systems and an LLM. CI providers remain authoritative for run state and evidence. CiAi normalizes provider data into typed contracts; model output does not grant permissions or turn arbitrary text into executable commands.

## 01 — See it in a minute

Requires the .NET 8 SDK.

Observe a real GitHub Actions run:

```bash
dotnet run --project samples/TheSingularityWorkshop.CiAi.ObserveRun -- TrentBest/TheSingularityWorkshop.CiAi 40027841199
```

Replace the example with a run ID that exists in the target repository. Public repositories can be queried without a token; set `CIAI_GITHUB_TOKEN` only when the target requires authentication. The client performs GET only.

Observe every matching workflow run, its exact attempt, jobs, and steps for one full commit SHA, then write JSON and Markdown evidence summaries:

```bash
dotnet run --project samples/TheSingularityWorkshop.CiAi.ObserveRun -- TrentBest/TheSingularityWorkshop.CiAi <full-40-character-commit-sha> artifacts/commit-observation.json
```

Use a real 40-character SHA in place of the placeholder. The observer uses bounded pagination and writes raw run/job evidence. To evaluate a policy, configure `CIAI_REQUIRED_WORKFLOW_PATHS` as a semicolon-separated list of exact workflow paths, for example:

```bash
export CIAI_REQUIRED_WORKFLOW_PATHS=".github/workflows/ci.yml;.github/workflows/benchmarks.yml"
dotnet run --project samples/TheSingularityWorkshop.CiAi.ObserveRun -- TrentBest/TheSingularityWorkshop.CiAi <full-40-character-commit-sha> artifacts/commit-observation.json
```

Only explicitly required workflow identities block the policy; unrelated runs remain visible as diagnostics. Selectors may bind a workflow to an exact run and attempt, for example `.github/workflows/ci.yml#123456789#2`. Missing, duplicate, non-terminal, unsuccessful, or incomplete required evidence fails closed. The report separates API discovery completeness, required-evidence completeness, terminality, and success. Without a configured required-workflow policy, `complete` is false. The report is a raw, read-only observation—not a release verdict or publication approval.

Run offline contract checks and pack the library:

```bash
dotnet run --project tests/TheSingularityWorkshop.CiAi.ContractTests --configuration Release
dotnet pack src/TheSingularityWorkshop.CiAi/TheSingularityWorkshop.CiAi.csproj --configuration Release
```

## 02 — Gate an existing branch on demand

The normal push and pull-request triggers remain limited to `master` and `development`. To run the same contract gate against another **existing branch** without editing workflow source:

1. Open **Actions → CiAi Contract Verification → Run workflow**.
2. Choose the workflow branch that contains the desired CI definition (normally `development`).
3. Optionally enter the existing branch name in `target_branch`, for example `agent/serialization-optimization`. Leave it blank to test the branch selected for the workflow run.
4. Run the workflow and inspect the run's **Summary** for the requested ref, resolved commit SHA, policy profile, required-check results, and evidence-completeness verdict.

For a supplied target, the workflow validates the branch name, resolves exactly one matching remote branch to a full commit SHA, checks out that SHA, and verifies that the checked-out `HEAD` equals the resolved SHA before running restore, build, contract tests, and package-artifact validation. The summary binds its result to the repository identity and full commit SHA. Unknown or invalid branch names fail before the gate runs. Slash-separated names are supported. The workflow uses read-only repository permissions and does not create/delete branches, merge code, or publish NuGet packages.

This is an initial branch-targeting entry point for **CiAi's own contract workflow**, not yet a universal registry or GitHub Actions host adapter for every ecosystem repository. Consumer repositories still need their own triggers/dispatch wiring until the adapter is proven and adopted. A successful run is evidence for the resolved commit, not release authorization. The dispatch path still needs an end-to-end run against a third branch before this capability is considered verified.

## 03 — Package

- Package ID: `TheSingularityWorkshop.CiAi`
- Current candidate: `0.1.0-alpha.1`
- Target framework: `.NET 8`
- Runtime dependency: [NuGet.Versioning 7.9.0](https://www.nuget.org/packages/NuGet.Versioning/), used for NuGet-native version parsing and precedence
- License: MIT

The package candidate is configured with repository metadata, release notes, symbols, and this README embedded in the NuGet package. The dependency-update evaluator uses Microsoft's `NuGet.Versioning` library rather than a hand-maintained approximation of NuGet version semantics. CI builds, runs contract checks, and packs the library. A passing pack job is not a NuGet publication.

## 04 — Architecture and trust boundary

- **CI provider:** source of truth for repository identity, workflow run state, commit SHA, conclusion, and run evidence.
- **CiAi:** validates and normalizes CI facts; the current GitHub Actions client is read-only.
- **HeadlessAi:** provider-neutral HTTP/LLM integration is a future CiAi integration, not yet called by this package.
- **ProtocolAi:** controlled vocabulary integration is not yet wired into CiAi.
- **FSM_API:** optional lifecycle coordination; not CI transport, persistence, or a distributed scheduler.
- **SimpleDemos:** contains a separate live provider-ingestion proof; it does not prove an end-to-end LLM-driven CiAi workflow.

The read-only GitHub Actions client validates provider identity and preserves unknown conclusions instead of treating them as success. Separately, `CiPipelineExecutor` on `development` can execute registered in-process delegates sequentially, blocks privileged capabilities, and records step outcomes; `CiRequiredEvidenceEvaluator` checks required facts against an exact repository and full commit SHA. These are bounded building blocks, not yet an end-to-end CI policy host: the executor report includes wall-clock timestamps; `bool false` and `CiStepResult.Failure(...)` fail a step, while `CiStepResult.Success()` explicitly succeeds. Other returned values are outputs, not status codes, and no GitHub Actions adapter yet makes this executor the authority for consumer build/test/coverage/docs/benchmark gates. Do not use the prototype executor for operations that report failure through integer exit codes or other non-boolean values unless an adapter maps them to `CiStepResult`. The observer client itself remains read-only and does not dispatch work, cancel runs, publish packages, or deploy applications.

## 05 — Source-derived contract documentation

- [Await and Gate contracts: durable dependency declarations, exact-source evidence, adjudication, and event receipts](docs/AWAIT_GATE_CONTRACTS.md)
- [Workflow domain map: 22 workflow families and cross-cutting contracts](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/CI_WORKFLOW_DOMAIN_MAP.md)
- [Declarative workflow plan validation: stable IDs, domain references, dependency graphs, and deterministic recipes](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/CI_WORKFLOW_DOMAIN_MAP.md#validated-workflow-plans)
- [Declarative CI builder and publication-authorization design](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/CI_BUILDER_DESIGN.md) — recipe-style pipeline direction. The definition builder, bounded in-process executor, and fail-closed required-evidence evaluator now exist on `development`; canonical deterministic reporting, provider-host integration, and publication remain incomplete.
- [CI user workflows and fluent vocabulary](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/CI_USER_WORKFLOWS.md) — user-centered workflows and the words the fluent API should express; proposed capabilities are clearly separated from implemented ones.
- [UML capability feasibility pilot](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/UML_CAPABILITY_PILOT.md)
- [Read-only CI observation UML and use case](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/UML/CiRunObservationUML.md)
- [RepositoryIdentity class companion](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/src/TheSingularityWorkshop.CiAi/RepositoryIdentity.md)
- [CiRunEvidenceVerifier class companion](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/src/TheSingularityWorkshop.CiAi/CiRunEvidenceVerifier.md)
- [Evidence reconciliation UML and use case](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/UML/CiRunEvidenceVerifierUML.md)
- [Artifact handoff contract types and use case](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/UML/ArtifactHandoffContractsUML.md)
