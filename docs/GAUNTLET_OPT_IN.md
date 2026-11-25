# Voluntarily opting a repository into Gauntlet

CiAi treats Gauntlet as an optional adversarial-verification capability. A repository owner opts in explicitly:

```csharp
using TheSingularityWorkshop.CiAi;

var repository = CiAi
    .WithGauntlet()
    .Build("owner/repository");
```

The equivalent composition form is:

```csharp
var repository = new CiAiRepositoryBuilder()
    .WithGauntlet()
    .Build("owner/repository");
```

The declaration is persisted in the deterministic `ciai.repository.v1` manifest as the versioned `Gauntlet.Registrar` extension. A repository that does not call `WithGauntlet()` has no Gauntlet registration. Repeating the opt-in is rejected as a duplicate rather than silently creating multiple providers.

## What opting in means

- The repository owner has declared that CiAi may discover and configure the Gauntlet provider for this repository.
- The host can select an immutable Gauntlet campaign profile and connect the provider to the existing CiAi exact-run evidence lane.
- The report remains advisory until a separately reviewed repository policy promotes it to a required check.
- CiAi must independently validate provider trust, repository, exact candidate SHA, artifact/manifest digests, profile identity, Gauntlet version, run/attempt, report digest, completeness, freshness, and replay status.
- A missing, stale, incomplete, mismatched, invalid, duplicated-conflicting, or untrusted report is non-passing and can never become a green badge.

## What opting in does not mean

Calling `WithGauntlet()` is declarative. It does not execute a campaign, download code, make a network request, modify a workflow, create credentials, change required checks, or authorize a GitHub write. A host adapter must be deliberately configured to run the campaign and publish its report through the existing evidence lane.

The intended stage order when the capability is enabled is:

```text
Build + ordinary tests
        |
        v
Gauntlet campaign (optional)
        |
        v
CiAi independently validates report and provenance
        |
        v
Existing CiAi policy / feedback lane
        |
        v
Documentation and downstream stages
```

When the capability is absent, the existing CiAi pipeline continues without Gauntlet. Gauntlet failure or provider unavailability is surfaced as advisory evidence during the initial adoption phase; it does not bypass or weaken existing required checks.

## Trust and execution boundary

Gauntlet currently has a deterministic in-process runner for Gauntlet-owned trusted probes. That runner is not a sandbox and does not execute candidate-supplied code. The repository's host-isolation request/receipt contracts are not proof of OS enforcement, and a production disposable-VM/strong-isolation adapter, durable replay protection, provider onboarding, and independent adversarial qualification remain release blockers.

Until those host gates are independently qualified, candidate-controlled execution must remain disabled. A CI workflow may orchestrate and transport reports, but it must not execute untrusted candidate code in the orchestration job.

## Ownership

- Gauntlet owns trusted campaign profiles, probes, findings, scoring inputs, and immutable run reports.
- The host owns sandbox provisioning, process isolation, resource limits, credentials, and cleanup.
- CiAi owns exact-run observation, independent evidence validation, gate policy, and the existing feedback lane.
- HeadlessAi may assess validated, pinned findings; model output cannot change a verdict.
- FSM_Execution may coordinate bounded repair and human handoff; it cannot authorize publication.
- Human approval remains authoritative for merge, publication, release, and deployment.

See [CiAi integration issue #71](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/71) and [Gauntlet release readiness](https://github.com/TrentBest/TheSingularityWorkshop.Gauntlet/blob/development/docs/RELEASE_READINESS.md).
