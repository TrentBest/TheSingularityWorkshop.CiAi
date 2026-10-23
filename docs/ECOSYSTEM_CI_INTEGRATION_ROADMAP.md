# Ecosystem CI Integration Roadmap

**Audit date:** 2026-10-23  
**Purpose:** Prioritize incremental adoption of TheSingularityWorkshop.CiAi across the workshop repositories without replacing existing CI authorities.

## Operating contract

- GitHub Actions (and each repository's existing build/test/package workflow) remains authoritative for execution.
- CiAi begins as a read-only observer and evidence correlator. A green run proves only the checks actually executed by that run.
- Repository declarations should eventually describe package identity/version, public capabilities, dependency constraints, examples, and test evidence.
- Source-graph builds that check out sibling repositories on a moving branch such as `development` are useful integration tests, but are not reproducible evidence unless the exact resolved commit SHAs are recorded.
- Do not enable release/publish operations through CiAi as part of the initial rollout. Publishing remains behind each repository's existing explicit approval gate.
- Add CiAi reporting alongside existing workflows first; do not make it a required merge gate until its evidence model and failure semantics are validated.

## Priority order

Priorities combine workflow complexity, cross-repository dependency depth, and value as a compatibility/evidence source. They are not a statement that a repository is release-ready.

### Tier 1 — first integration pilots

| Priority | Repository | CI evidence observed | Why it matters | First CiAi milestone |
|---|---|---|---|---|
| 1 | [GUI](https://github.com/TrentBest/TheSingularityWorkshop.GUI) | Package/test, source-derived diagrams, solution-derived UML, documentation quality; sibling-source checkout in package workflow. Four workflows ran successfully on the same observed feature-branch SHA on 2026-10-23. | Many evidence types, generated living documentation, and dependencies on sibling repositories. | Catalog workflow identities and evidence types; correlate the same commit across workflow runs; report missing/stale evidence without blocking CI. |
| 2 | [FSM_COS](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS) | Package/publish workflow plus CodeQL; source integration builds/tests FSM_COS with FSM_API, FSM_UserIO, and MicroBundleDomain. | Core composition/runtime boundary with source and published-package compatibility lanes. | Model source-graph vs public-package evidence as separate capabilities; preserve the failing step and run URL in the report. |
| 3 | [AnyApp](https://github.com/TrentBest/AnyApp) | Two workflows on default branch; development .NET workflow checks out a deep sibling graph and builds Renderer/AnyApp in source mode. Baseline workflow exercises multiple publish modes. | Deep dependency graph and high risk of source/version drift. | Declare direct/transitive repository relationships and compare the checked-out dependency SHAs to catalog expectations. |
| 4 | [Visualize](https://github.com/TrentBest/TheSingularityWorkshop.Visualize) | Main build/package workflow plus observer-presentation proof and WPF/GPU proof on development. | Cross-repository rendering and presentation proofs need precise evidence boundaries. | Represent each proof as a separate capability and link its successful run to exact source revisions. |
| 5 | [WebApp](https://github.com/TrentBest/TheSingularityWorkshop.WebApp) | Published-package compatibility and source-integration lanes; source lane checks out FSM_API, FSM_UserIO, MicroBundleDomain, FSM_COS, and GUI. | A real application-level compatibility consumer. | Compare source-graph and published-package results; keep them distinct in readiness summaries. |
| 6 | [Renderer](https://github.com/TrentBest/TheSingularityWorkshop.Renderer) | Build/test/package workflow, coverage, and explicit FSM_COS composition proofs with/without rendering dependencies. | Useful boundary proof between textual AI-facing rendering and optional presentation integrations. | Declare dependency-optional behavior and link both composition proofs as distinct evidence. |

### Tier 2 — foundational compatibility sources

- [FSM_REST](https://github.com/TrentBest/TheSingularityWorkshop.FSM_REST): package/test and verification workflows; recent development package runs succeeded.
- [FSM_Serialization](https://github.com/TrentBest/TheSingularityWorkshop.FSM_Serialization): build/package plus a separate publish workflow.
- [MicroBundleDomain](https://github.com/TrentBest/TheSingularityWorkshop.MicroBundleDomain): foundational domain package used by multiple source graphs.
- [MicroBundleRepository](https://github.com/TrentBest/TheSingularityWorkshop.MicroBundleRepository): core and package workflows; repository/storage boundary.
- [ProtocolAi](https://github.com/TrentBest/TheSingularityWorkshop.ProtocolAi) and [GrammarAi](https://github.com/TrentBest/TheSingularityWorkshop.GrammarAi): focused build workflows and AI contract dependencies.
- [HeadlessAi](https://github.com/TrentBest/TheSingularityWorkshop.HeadlessAi): build, test, coverage, and pack; keep its core provider-neutral and dependency-free.
- [Physics](https://github.com/TrentBest/TheSingularityWorkshop.Physics): large build workflow that checks out MicroBundleDomain and Experiences, emits and verifies parameterized Mandelbrot run records, and exercises tests.
- [Gestures](https://github.com/TrentBest/TheSingularityWorkshop.Gestures): build/package plus benchmark and motion-lab workflows; generates living SVG evidence from source behavior.
- [WebPage](https://github.com/TrentBest/WebPage): Azure Static Web Apps deployment workflow plus .NET tests; deployment evidence must remain separate from test evidence.
- [FSM_API](https://github.com/TrentBest/FSM_API): core dependency used across many integration graphs.

## Verified failure signals from the audit

These are observations from specific runs, not inferred root causes beyond the log evidence.

- **FSM_COS, run [40040760432](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS/actions/runs/40040760432), 2026-10-23:** source-integration job passed. The main package job failed at the Markdown branding check because many files lacked exactly one required Workshop footer marker. The public-package compatibility lane built, tested, and packed, then failed package-content verification because `docs/UML_VISUAL_STYLE.md` links to `../DOCUMENTATION_STANDARD.md`, which resolves to a file absent from the package. This is an excellent example of why CiAi must preserve job/step-level evidence instead of flattening a workflow to a single green/red status.
- **AnyApp, run [39899276153](https://github.com/TrentBest/AnyApp/actions/runs/39899276153), 2026-10-23:** sibling checkout and source-graph restore/build steps passed; compilation of the test project failed because `ExperiencePublisherTests.PublishableBundle` has no `ArbitrationCalls` member at two call sites. This is currently a source/test contract mismatch, not evidence of a failing production runtime.
- **WebApp, run [39515049934](https://github.com/TrentBest/TheSingularityWorkshop.WebApp/actions/runs/39515049934), 2026-10-20:** the source graph restored and built, but the source-graph test step failed. The available log retrieval did not yield enough usable detail to state the cause; inspect the run directly before assigning a diagnosis.
- **GUI, run [40040506075](https://github.com/TrentBest/TheSingularityWorkshop.GUI/actions/runs/40040506075), plus [source-derived diagrams](https://github.com/TrentBest/TheSingularityWorkshop.GUI/actions/runs/40040506177), [solution-derived UML](https://github.com/TrentBest/TheSingularityWorkshop.GUI/actions/runs/40040506196), and [documentation quality](https://github.com/TrentBest/TheSingularityWorkshop.GUI/actions/runs/40040506275): all four observed workflows passed on commit `affe175400c97549b4569a47b361e0cbb9ff0655`. That commit is on a working feature branch; branch reconciliation remains a separate workflow-management task.

## Cross-cutting CI risks

1. **Moving sibling branches.** Several source-integration workflows check out `development` from dependencies. Record each resolved dependency commit SHA in a machine-readable run manifest; otherwise, the same consumer commit can produce different results at different times.
2. **Mutable action tags.** The inspected Physics and Gestures workflows use major-version action tags (for example, `actions/checkout@v7` and `actions/setup-dotnet@v6`). GUI's package workflow explicitly checks immutable action pins. Standardize the immutable-SHA check and pinned references across workflows before using CI evidence for strong release claims.
3. **Different evidence classes.** Unit tests, source-graph integration, public NuGet compatibility, package-content verification, generated UML/doc checks, GPU/presentation proofs, and deployment are not interchangeable. CiAi must preserve the class, commit, workflow/run, job, step, and conclusion.
4. **Status flattening.** A failed workflow can include successful source-graph tests but fail documentation or package validation. Store run summary and step-level outcomes so reports explain what failed.
5. **Branch scope.** Some workflows run on all branches while others only target `development` or `master`. CiAi must not treat absent runs as failures; it should distinguish not configured, not triggered, pending, skipped, cancelled, and failed.
6. **Publish safety.** Keep publication behind explicit human approval and existing repository-specific gates. CiAi's initial observation/reporting integration must not gain write permissions or publish authority.

## Rollout plan

### Phase 0 — inventory only
- Maintain a catalog of repositories, workflow files, triggers, action pinning, dependency checkouts, evidence outputs, package IDs, and release gates.
- Report data coverage and uncertainty. The first inventory is a targeted pass across the primary .NET ecosystem, not a complete verified census of every repository or every branch.

### Phase 1 — read-only observation pilot
- Start with GUI and FSM_COS, then AnyApp.
- Fetch workflow runs using the provider client and validate repository, run ID, head SHA, status/conclusion, and evidence URL.
- Emit a normalized report keyed by repository + commit + workflow/run, including job/step failures where available.
- Keep all existing CI checks authoritative; no required status check yet.

### Phase 2 — declarations and compatibility matrix
- Add declarations for package identity/version, capabilities, dependency constraints, examples, and evidence expectations.
- Record exact resolved SHAs for sibling-source checkouts.
- Compare source-graph compatibility with published-package compatibility as separate dimensions.
- Add ProtocolAi operation vocabulary through the existing policy boundary; do not invent a second operation registry.

### Phase 3 — opt-in cross-repository gate
- Run a small compatibility matrix for selected dependency edges (GUI ↔ Visualize/Renderer; FSM_API/MicroBundleDomain ↔ FSM_COS; FSM_COS/Renderer/Visualize ↔ AnyApp/WebApp).
- Start as advisory output. Promote to a required check only after false positives, stale evidence, missing runs, cancellations, and provider errors are tested.
- Keep publish operations disabled until a separate explicit authorization design and human approval.

## CiAi readiness context

As of this audit, CiAi implementation readiness remains **30%** and package preparation **70%**. Its read-only GitHub Actions run observer and contract-verification workflow are useful foundations, but a complete correlated request-to-authoritative-result round trip and durable cross-repository evidence model are not yet implemented. A green CiAi package workflow does not mean ecosystem integration is complete.
