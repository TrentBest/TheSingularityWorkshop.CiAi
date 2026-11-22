# CiAi ecosystem enrollment matrix

## What "using CiAi" means

Enrollment is deliberately split into three independent capabilities:

1. **Repository contract** — a checked-in `.ciai/repository.json`, managed README badges, and the reusable read-only repository workflow.
2. **Trusted observation** — an observer profile that binds provider run, attempt, workflow revision, required job, artifacts, exact SHA identities, and protected workflow trust.
3. **Autonomous development** — trusted CI evidence can enter bounded execution and repair, with host-owned human approval and review.

A repository may be enrolled at level 1 without being trusted for level 2 or authorized for level 3.

## Active adoption

| Repository | Current state | Evidence / next step |
|---|---|---|
| `TheSingularityWorkshop.CiAi` | Self-hosting draft PR #77 | Manifest, fluent configuration, README stamper/CLI, reusable workflow, observer profiles, trust and PR identity contracts. Exact head `2d66e8b432be0ba5cf83c7108f259f3bce672e64` passed 74 individual checks / 111 records / 0 failures; its manifest resolves the `ciai-default` observer profile. |
| `raWWar` | Repository-contract draft PR #10 | 14 README files stamped; `MicroBundleDomain` dependency declared; reusable workflow pinned to the exact CiAi commit. Repository contract, build, and Pages PR runs are green. External observer profile exists, but its PR evidence remains non-actionable until host-side workflow and merge-parent identities are independently bound. |
| `AnyApp` | Repository-contract draft PR #63 | 2 README files stamped; dependency manifest reflects direct package and source-MicroBundle references, including `FSM_REST` and `MicroBundleDomain`; `anyapp-development-v1` observer profile declared; reusable workflow and existing exact-run observer are pinned to `2d66e8b432be0ba5cf83c7108f259f3bce672e64`. Repository-contract and Windows .NET build/test/publish runs are green. The executable measured 134.70 MB, above the earlier ~60 MB self-contained target; track that as a separate optimization follow-up. |

## Next active repositories

These are active Workshop workstreams from the current development program. They are not represented as enrolled until their own manifest and workflow PRs exist.

### Atomic-first candidates

Audit their project files and workflow definitions first; if a repository has no dependency graph, enroll it before dependency-heavy consumers.

- `FSM_API`
- `TheSingularityWorkshop.Physics`
- `TheSingularityWorkshop.Ontology`
- `TheSingularityWorkshop.Learning`
- `TheSingularityWorkshop.DataWarehouse`
- `TheSingularityWorkshop.Hermit`

### Runtime and AI foundation

Enroll after the atomic audit and preserve the dependency graph rather than flattening it.

- `TheSingularityWorkshop.FSM_COS`
- `TheSingularityWorkshop.FSM_Execution`
- `FSM_UserIO`
- `TheSingularityWorkshop.HeadlessAi`
- `TheSingularityWorkshop.ProtocolAi`
- `TheSingularityWorkshop.GrammarAi`
- `TheSingularityWorkshop.CommandAi`

### MicroBundle, GUI, and host infrastructure

- `TheSingularityWorkshop.MicroBundleDomain`
- `TheSingularityWorkshop.MicroBundleRepository`
- `TheSingularityWorkshop.MicroBundleIngestor`
- `TheSingularityWorkshop.Experiences`
- `TheSingularityWorkshop.GUI`
- `TheSingularityWorkshop.Renderer`
- `TheSingularityWorkshop.Visualize`
- `TheSingularityWorkshop.WebPage`
- `TheSingularityWorkshop.ServerApp`

### Domain, application, and experience workstreams

- `TheSingularityWorkshop.Gestures`
- `TheSingularityWorkshop.Economy`
- `TheSingularityWorkshop.Profiles`
- `TheSingularityWorkshop.FSM_REST`
- `TheSingularityWorkshop.CreatorNetwork`
- `TheSingularityWorkshop.MyVR`
- `TheSingularityWorkshop.WebApp`
- `TheSingularityWorkshop.SingularityBot`
- `StoryBuilder`

### Benchmark repositories

- `TheSingularityWorkshop.Benchmarks.GUI`
- DataWarehouse benchmark workstream

## Rollout order and gates

1. Finish the raWWar proving-ground performance comparison. Its current repository/build contracts are green; the performance threshold itself is still a separate gate.
2. Audit and enroll atomic repositories with no dependency graph.
3. Enroll runtime and AI foundation repositories in dependency order.
4. Enroll MicroBundle, GUI, host, and domain consumers.
5. Enroll benchmark repositories and bind their results to exact source/package identities.

For each repository, the enrollment PR must:
- derive dependencies from its actual project/manifest files;
- add `.ciai/repository.json` and the reusable workflow caller;
- stamp every repository-owned `README.md` through the CiAi tool;
- pin the reusable workflow and tool source to an immutable CiAi commit;
- preserve its own build/test workflow and release boundaries.

## Safety and branch policy

- The reusable repository workflow is read-only. It checks manifests and README badge state; it does not commit, merge, publish, release, or deploy.
- A green repository-contract check does not imply trusted test evidence or autonomous repair authorization.
- Candidate provenance cannot establish workflow trust. The host must independently retrieve provider identities and approved workflow content.
- NuGet publication, merges, releases, deployments, canonical-branch writes, and branch deletion remain separately gated.
- Draft PR branches temporarily make the Sith exact-two-branch policy fail. Keep those branches only while their PRs are active; flag them ready for batch cleanup after review/merge, and do not delete them prematurely.
