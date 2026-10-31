<!-- The Singularity Workshop moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# The Singularity Workshop CiAi

[![CiAi Contract Verification](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/workflows/ci.yml/badge.svg?branch=development)](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/workflows/ci.yml)
[![Cog Ecosystem Integration](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/workflows/cog-ecosystem.yml/badge.svg?branch=development)](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/workflows/cog-ecosystem.yml)

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

The normal push and pull-request triggers remain limited to `master` and `development`. For every manual or host-initiated `workflow_dispatch`, use `development` as the trusted workflow-definition ref; never select the candidate feature branch as the workflow ref. The optional `target_branch` input selects the code to test:

1. Open **Actions → CiAi Contract Verification → Run workflow**.
2. Select `development` as the workflow branch.
3. Optionally enter an existing branch in `target_branch`, for example `agent/serialization-optimization`. Leave it blank to test `development` itself.
4. Run the workflow and inspect the **Summary** for the trusted workflow revision, requested candidate ref/SHA, policy profile, required-check results, and evidence-completeness verdict.

The correlated host-dispatch contract additionally supplies all five `ciai_*` inputs (request, correlation, task, candidate SHA, attempt) together. That input mapper exists, but the authenticated host dispatcher is not implemented.

For a supplied target, the workflow validates the candidate branch name, resolves exactly one matching remote branch to a full commit SHA, checks out that SHA, and verifies that the checked-out `HEAD` equals the resolved SHA before running restore, build, contract tests, and package-artifact validation. The summary records the trusted workflow-definition ref/SHA separately from the candidate ref/SHA. Unknown or invalid branch names fail before the gate runs. Slash-separated names are supported. The workflow uses read-only repository permissions and does not create/delete branches, merge code, or publish NuGet packages. GitHub requires this workflow file to exist on the default branch (`master`) before `workflow_dispatch` is available; the current draft has not been merged there, so no remote/manual dispatch is currently available.

This is an initial branch-targeting entry point for **CiAi's own contract workflow**, not yet a universal registry or GitHub Actions host adapter for every ecosystem repository. Consumer repositories still need their own triggers/dispatch wiring until the adapter is proven and adopted. A successful run is evidence for the resolved commit, not release authorization. The dispatch path still needs an end-to-end run against a third branch before this capability is considered verified. The workflow also accepts an opt-in correlated CiAi input group (`ciai_request_id`, `ciai_correlation_id`, `ciai_task_id`, `ciai_candidate_sha`, and `ciai_attempt_number`). When present, all five are required together. The host dispatches the trusted workflow from `development`, passes the candidate feature branch separately, and the workflow re-resolves that branch to the exact candidate SHA before checkout/build; workflow-definition ref/SHA are recorded separately from candidate provenance. This is the workflow-side contract only—the authenticated HTTP dispatch client and durable dispatch-intent/reconciliation ledger are not yet implemented. GitHub requires the workflow file to exist on the repository default branch (`master`) before `workflow_dispatch` can be triggered; this draft does not merge it there, so remote dispatch is not yet available. Evidence packet v2 now carries separate `workflowRef`/`workflowSha` and `candidateRef`/`candidateSha` fields. For trusted-ref dispatch, `CandidateCheckoutVerified` is true only when the registered branch-resolution and checkout-verification steps both complete successfully in the registered job. Checkout verification is derived from the exact observed resolution/verification steps and remains true if a later build/test fails; `IsSuccessful` still remains false for a failed job/run. A successful packet is not a workflow-trust verdict: the host must validate the workflow SHA against its trusted revision policy. Foundry must consume v2 and reconcile both SHA roles. `FromJson` rejects v1 rather than silently treating the workflow SHA as the candidate SHA.

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

The read-only GitHub Actions client validates provider identity and preserves unknown conclusions instead of treating them as success. `CiWorkflowEvidencePacket` v2 adds a versioned, exact-run-attempt JSON envelope with separate workflow-definition and candidate ref/SHA roles, derived candidate-checkout verification, jobs/steps, completeness, diagnostics, and artifact references. It remains evidence only; it does not dispatch repairs or authorize merge/publication. See [the packet contract](src/TheSingularityWorkshop.CiAi/CiWorkflowEvidencePacket.md) and [candidate checkout evidence](src/TheSingularityWorkshop.CiAi/CiWorkflowCandidateCheckoutEvidence.md). `FoundryArtifactReadyNoticeClient` now implements the metadata-only HTTPS sender for Foundry's normative v1 artifact-ready callback, with host-supplied short-lived bearer credentials, exact notice/idempotency receipt matching, bounded response parsing, and explicit accepted/duplicate/retryable/policy outcomes. See [the sender contract](src/TheSingularityWorkshop.CiAi/FoundryArtifactReadyNoticeClient.md). The client performs one transport attempt; `FileFoundryArtifactReadyNoticeOutbox` preserves exact notice bytes across restart, and `FileFoundryArtifactIngressReceiptLedger` suppresses replay only after a matching accepted/duplicate receipt. `FoundryArtifactReadyNoticeDeliveryCoordinator` now composes these components for one bounded sequential pass, persists only accepted/duplicate receipts, and stops on global auth/back-pressure/transient failures; the host still owns scheduling and retry policy. See [the delivery coordinator contract](src/TheSingularityWorkshop.CiAi/FoundryArtifactReadyNoticeDeliveryCoordinator.md) and the [artifact handoff UML](docs/UML/ArtifactHandoffContractsUML.md). Immutable artifact hosting, concrete credential provisioning, provider acknowledgement transport, and the remote Foundry receiver remain unimplemented; a receiver receipt is not proof of artifact retrieval or CI success. The bounded exact-target rerun request and host authorization policy are documented in [the rerun request contract](src/TheSingularityWorkshop.CiAi/CiWorkflowRerunRequest.md); this contract does not itself authenticate credentials or dispatch workflows. The opt-in workflow input mapping is documented in [CiWorkflowDispatchInputContract](src/TheSingularityWorkshop.CiAi/CiWorkflowDispatchInputContract.md); the workflow now validates correlated request/task/attempt identity and the exact event/ref/candidate SHA, but the authenticated HTTP dispatcher and durable dispatch-attempt ledger are still unimplemented. Separately, `CiPipelineExecutor` on `development` can execute registered in-process delegates sequentially, blocks privileged capabilities, and records step outcomes; `CiRequiredEvidenceEvaluator` checks required facts against an exact repository and full commit SHA. These are bounded building blocks, not yet an end-to-end CI policy host: the executor report includes wall-clock timestamps; `bool false` and `CiStepResult.Failure(...)` fail a step, while `CiStepResult.Success()` explicitly succeeds. Other returned values are outputs, not status codes, and no GitHub Actions adapter yet makes this executor the authority for consumer build/test/coverage/docs/benchmark gates. Do not use the prototype executor for operations that report failure through integer exit codes or other non-boolean values unless an adapter maps them to `CiStepResult`. The observer client itself remains read-only and does not dispatch work, cancel runs, publish packages, or deploy applications.

## 05 — Source-derived contract documentation

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

---

<!-- THE SINGULARITY WORKSHOP FOOTER -->
<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop" width="200" /></p>
