# CiAi ecosystem integration plan

**Status:** coordination plan; no consumer integration is claimed yet.  
**Owner:** CiAi.  
**Rule:** CI remains owned by each repository. CiAi is an optional, read-only evidence observer—not a runtime dependency, build orchestrator, merge bot, or release authority.

## Why these repositories

The visitor experience links four neighboring capabilities because they make CiAi's role concrete:

| Repository | Owns | Existing master workflow | CiAi evidence opportunity |
|---|---|---|---|
| [Learning](https://github.com/TrentBest/TheSingularityWorkshop.Learning) | Interactive learning experiences and Pages content | `.github/workflows/pages.yml` | Observe source validation and Pages workflow outcomes for the exact source commit; keep deployment success distinct from content correctness. |
| [Physics](https://github.com/TrentBest/TheSingularityWorkshop.Physics) | Physical models, experiments, and focused tests | `.github/workflows/build.yml` | Observe build/test evidence and preserve failed experiment/test lanes. |
| [GUI](https://github.com/TrentBest/TheSingularityWorkshop.GUI) | Semantic GUI model and platform manifestations | `.github/workflows/package.yml` | Observe package/build/test lanes and distinguish Windows WPF build evidence from cross-platform Core/Blazor evidence. |
| [FSM_API](https://github.com/TrentBest/FSM_API) | State-machine runtime and compatibility surface | `.github/workflows/dotnet.yml` | Observe target-framework/build/test evidence and keep benchmark evidence separate from correctness gates. |

These workflow paths were found in each repository's current `master` workflow directory on 2026-11-07. They are discovery starting points, not an assertion that every workflow is a required release gate.

## Integration contract

1. **No package dependency.** Consumer repositories do not add a runtime or build-time NuGet dependency on CiAi merely to expose CI evidence.
2. **Provider authority remains local.** Each repository's GitHub Actions workflows continue to build, test, package, and deploy according to that repository's own policy.
3. **Exact source identity.** Every observation names the canonical `owner/repository` and full 40-character commit SHA. Evidence from different SHAs is never combined.
4. **Explicit workflow policy.** Required workflow paths are supplied by the repository owner. Discovery of unrelated workflows is diagnostic; it does not silently make them blockers.
5. **Fail closed.** Missing, duplicate, stale, pending, skipped, cancelled, neutral, unknown, malformed, incomplete, or mismatched required evidence remains non-passing.
6. **Read-only first.** Initial adoption observes and reports. It does not dispatch builds, write repository refs, open or merge PRs, publish packages, deploy sites, or grant release approval.
7. **Evidence before claims.** A consumer pilot is verified only when source, negative tests, and a successful run tied to the exact implementation commit demonstrate the behavior.

## Adoption sequence

- **Stage A — CiAi self-observation:** keep the observer and contract tests authoritative for CiAi's own read-only evidence path.
- **Stage B — one consumer pilot:** use the existing AnyApp pilot to prove a consumer-owned workflow can hand CiAi an exact target identity and receive a trustworthy report.
- **Stage C — one small ecosystem pilot:** select one of Physics or FSM_API after confirming its required workflow identities and expected target frameworks with that repository's owner.
- **Stage D — GUI and Learning:** add their domain-specific evidence mapping only after the smaller pilot demonstrates stable request, report, and failure semantics.

Do not attempt a universal workflow registry or broad multi-repository rollout before a single consumer pilot is end-to-end verified.

## Current discovery

A repository code search for `CiAi` returned no matches in Learning, Physics, GUI, or FSM_API on 2026-11-07. That means there is no discovered direct source integration to preserve or extend. It does not mean those repositories need a dependency. The correct next step is a small, opt-in observer workflow only where it adds useful evidence beyond each repository's existing CI.

## Visitor experience

The [CiAi visitor page](../index.html) demonstrates the observer boundary with synthetic data and links to the neighboring repositories. It is an educational walkthrough, not a live connection to their workflows.
