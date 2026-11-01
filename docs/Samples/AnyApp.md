<!-- The Singularity Workshop animated moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# AnyApp: First CiAi Consumer

## Purpose

AnyApp is the first source-based consumer pilot for CiAi. It exercises the existing read-only GitHub Actions observer against a real .NET desktop application that builds, tests, and publishes a Windows x64 single-file executable during development CI.

This is now an implemented integration scaffold on AnyApp's development branch, not merely a proposed sample. Its first workflow run is the verification step; do not describe the pilot as proven until a real evidence artifact has been inspected.

## Live consumer integration

- Consumer repository: [TrentBest/AnyApp](https://github.com/TrentBest/AnyApp)
- Consumer workflow: [CiAi AnyApp Evidence](https://github.com/TrentBest/AnyApp/blob/development/.github/workflows/ciai-evidence.yml)
- Consumer documentation: [CiAi Evidence Integration](https://github.com/TrentBest/AnyApp/blob/development/docs/CIAI_EVIDENCE_INTEGRATION.md)
- Authoritative build workflow: [AnyApp .NET workflow](https://github.com/TrentBest/AnyApp/blob/development/.github/workflows/dotnet.yml)
- CiAi observer source: [ObserveRun sample](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/tree/94e0a13ae171de4fce7eb5989e1dea4fe72cc16c/samples/TheSingularityWorkshop.CiAi.ObserveRun)
- Pinned CiAi source commit: `94e0a13ae171de4fce7eb5989e1dea4fe72cc16c`

On a push to AnyApp development, the consumer workflow waits for the authoritative .NET workflow to complete for the same full commit SHA. It resolves the exact run and attempt when unambiguous, checks out CiAi at the immutable commit above, invokes the existing observer, and uploads JSON/Markdown evidence reports.

The observer uses the repository-scoped GitHub token with read-only Actions and contents permissions. It does not execute AnyApp's build and does not receive permission to merge, publish, create releases, or deploy.

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
5. Keep the observer read-only and the integration non-blocking until evidence semantics have been exercised against real consumer runs.

This is a source-based pilot, not a completed reusable GitHub Actions host adapter. AnyApp's build/test workflow remains authoritative. No NuGet package publication, GitHub Release, deployment, or merge is part of this example.

<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop avatar" width="200" /></p>
