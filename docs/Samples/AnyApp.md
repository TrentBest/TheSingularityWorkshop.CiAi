<!-- The Singularity Workshop animated moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# AnyApp: First CiAi Consumer

## Purpose

AnyApp is the first self-building consumer being wired to CiAi. The first slice is a source-based, read-only observer; the next slice makes AnyApp's existing Windows CI produce exact-run workflow provenance and TRX diagnostics so CiAi can normalize real test failures. GitHub Actions remains the authoritative build/test executor.

This is a staged integration, not a completed self-repair loop. AnyApp PR #37 contains the producer-side provenance/TRX change. Its first PR workflow run has already emitted a provenance artifact; the test lane and TRX artifact remain under runtime validation. Do not claim a repair cycle until trusted ingestion, HeadlessAi proposal, host authorization/application, and exact-candidate revalidation have all been exercised.

## Live consumer integration

- Consumer repository: [TrentBest/AnyApp](https://github.com/TrentBest/AnyApp)
- Consumer workflow: [CiAi AnyApp Evidence](https://github.com/TrentBest/AnyApp/blob/development/.github/workflows/ciai-evidence.yml)
- Consumer documentation: [CiAi Evidence Integration](https://github.com/TrentBest/AnyApp/blob/development/docs/CIAI_EVIDENCE_INTEGRATION.md)
- Authoritative build workflow: [AnyApp .NET workflow](https://github.com/TrentBest/AnyApp/blob/development/.github/workflows/dotnet.yml)
- CiAi observer source: [ObserveRun sample](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/tree/94e0a13ae171de4fce7eb5989e1dea4fe72cc16c/samples/TheSingularityWorkshop.CiAi.ObserveRun)
- Pinned CiAi source commit: `94e0a13ae171de4fce7eb5989e1dea4fe72cc16c`

On a push to AnyApp development, the consumer workflow waits for the authoritative .NET workflow to complete for the same full commit SHA. It resolves the exact run and attempt when unambiguous, checks out CiAi at the immutable commit above, invokes the existing observer, and uploads JSON/Markdown evidence reports.

The observer uses the repository-scoped GitHub token with read-only Actions and contents permissions. It does not execute AnyApp's build and does not receive permission to merge, publish, create releases, or deploy.

## Provenance and TRX producer slice

AnyApp PR [#37 — Add provenance-bound test diagnostics for CiAi](https://github.com/TrentBest/AnyApp/pull/37) updates the existing .NET workflow to emit:

- `ciai-workflow-provenance-{runId}-{runAttempt}`, containing schema `ciai.workflow-provenance.v1` with repository, run/attempt, workflow-definition ref/SHA, event identity, PR head/base when applicable, and actual checkout SHA.
- `ciai-test-results-{runId}-{runAttempt}-build`, containing the AnyApp test project's TRX output.

The TRX upload runs with `if: always()`; the original test command's nonzero exit code remains authoritative. Missing TRX is incomplete evidence, not a pass. The producer does not download artifacts, verify provider ZIP digests, extract archives, or assert workflow-definition trust.

The first producer execution is [AnyApp PR run 41282827626](https://github.com/TrentBest/AnyApp/actions/runs/41282827626). It successfully uploaded provenance artifact ID `13073991348` with provider digest `sha256:d9f51a7bef119a3fdd9c30d35163693534475dfd7d24a19fc3201ec292def6d8`. The downloaded ZIP contained the expected JSON schema and exact run/attempt/checkout identities. Because this was a pull-request event, its workflow ref was `refs/pull/37/merge`; it proves artifact production, not trusted workflow authority. The host must validate workflow ref/SHA against protected control-plane policy.

## First verified observation

The first completed consumer observation is [CiAi run 41281306728](https://github.com/TrentBest/AnyApp/actions/runs/41281306728), for AnyApp commit `4d40f584fa4011179a731962afdd755fa85e8063`. It produced the artifact `CiAi-AnyApp-evidence-4d40f584fa4011179a731962afdd755fa85e8063`.

The observer correlated authoritative AnyApp run [41281306660, attempt 1](https://github.com/TrentBest/AnyApp/actions/runs/41281306660) and reported:

- Discovery complete: true
- Required evidence complete: true
- Workflow terminal: true
- Workflow successful: false
- Required-workflow state: failed
- Overall evidence policy complete: false

It retained the exact repository, source SHA, workflow path, run ID/attempt, job ID, job conclusion, step results, and authoritative run/job URLs. The observer workflow itself completed successfully because it is advisory; its report correctly says the AnyApp build/test lane failed. This is the intended distinction between successfully collecting evidence and the observed workflow being successful.

The first observed .NET run built AnyApp but failed two integration tests before EXE publishing. A later AnyApp workflow revision now attempts a diagnostic executable publish after test failure, without changing the failed CI verdict. That diagnostic behavior remains pending a completed run.

## What the first vertical slice proves

- A consumer can use CiAi from pinned source without depending on an unpublished NuGet package.
- Provider execution remains in the consumer's existing GitHub Actions workflow.
- CiAi can collect matching workflow runs for an exact source commit and preserve run-attempt, job, and step evidence.
- Required workflow evidence is evaluated against the exact repository and full commit SHA.
- The integration remains advisory: its result is reported separately and does not change the AnyApp build verdict.

## Declared AnyApp evidence lanes

| Lane | Provider workflow | Expected interpretation |
|---|---|---|
| Build, tests, Windows executable publish | `.github/workflows/dotnet.yml` | Exact required workflow evidence in the first pilot |
| Publish-size baseline | `.github/workflows/baseline.yml` | Separate master/manual baseline; may be not-triggered for a development SHA |
| Windows release candidate | `.github/workflows/release-executable.yml` | Release-published trigger; not expected during ordinary development |

Do not collapse absent lanes into success. The current sample invokes the existing raw observer and requires the exact .NET run/attempt. It does not yet expose a reusable typed AnyApp lane manifest; CiAi should generalize that contract if the first real runs show the current API is insufficient.

## How users can inspect the example

1. Open AnyApp **Actions → CiAi AnyApp Evidence**.
2. Select the evidence run for the commit being investigated.
3. Download the `CiAi-AnyApp-evidence-<commit-sha>` artifact.
4. Inspect `ciai-anyapp-evidence.json` for machine-readable facts and `ciai-anyapp-evidence.md` for the human-readable report.
5. Follow the included provider evidence links to verify every claim against GitHub Actions.

The source SHA in the report must match the AnyApp commit. A report for another commit is not evidence for this build.

## Known gaps to drive CiAi development

1. Promote the current per-workflow selector into a reusable typed lane declaration with explicit missing, not-triggered, skipped, cancelled, unavailable, unknown, failed, and successful states.
2. Correlate all declared lanes (build/test, publish baseline, release candidate) without pretending untriggered lanes failed.
3. Capture and report resolved SHAs for AnyApp's sibling source checkouts, not only the AnyApp source SHA.
4. Make provider evidence and artifact links easy to consume in a stable JSON/Markdown report contract.
5. Connect the trusted host artifact retriever to CiAi's provenance/TRX parser; verify provider ZIP digests, safe extraction, exact job ID/name binding, and workflow-definition trust.
6. Deliver the normalized diagnostic report into the same Foundry task, then drive a bounded HeadlessAi proposal through independent host authorization and exact-SHA revalidation.
7. Keep the observer read-only and the integration non-blocking until evidence semantics have been exercised against real consumer runs.

This is a source-based consumer and diagnostic-producer pilot, not a completed reusable GitHub Actions host adapter or self-repair loop. AnyApp's build/test workflow remains authoritative. No NuGet package publication, GitHub Release, deployment, or merge is part of this example.

<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop avatar" width="200" /></p>
