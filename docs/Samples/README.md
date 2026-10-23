# CiAi Repository Integration Samples

This directory contains one repository-specific proposal per repository visible in the connected TrentBest GitHub inventory at the time these samples were drafted.

These are **integration plans**, not assertions that every repository already has a CiAi client, a complete workflow audit, or machine-readable capability declarations. Several repository roles are inferred from names and prior ecosystem work and must be confirmed by their maintainers.

## How to use these samples

1. Open the repository's document below.
2. Confirm its purpose, supported runtimes, packages, CI workflows, and dependency graph against the current source.
3. Preserve the repository's own CI as the authority for build/test/package/deploy execution.
4. Add stable evidence metadata and resolved dependency SHAs before claiming cross-repository compatibility.
5. Integrate CiAi in read-only advisory mode first. Do not enable merge gating, publishing, or deployment authority as part of this rollout.

## Integration principles

- **Provider CI executes; CiAi observes, normalizes, correlates, and reports.**
- Evidence is tied to repository, commit, workflow, run ID/attempt, and job/step where available.
- A green run proves only the checks that actually ran.
- Missing workflow, skipped job, cancellation, unavailable runner, and unknown status remain distinct states.
- Moving sibling branch refs must be accompanied by the resolved SHA for reproducibility.
- Benchmark evidence is not a substitute for correctness tests.
- Host-specific checks (Unity, Revit/Dynamo, VR, GPU) must declare prerequisites and distinguish unavailable environments from failures.
- Publishing and deployment remain explicitly gated outside CiAi's authority.

## Repository samples

- [Tutorials](./Tutorials.md) — Tutorials/examples repository; CiAi should treat tutorial builds as reproducibility and documentation evidence.
- [TrentCoRetailManager](./TrentCoRetailManager.md) — Legacy retail-management application; integrate only if it remains actively maintained, with its own build/test evidence.
- [ConstructorBots](./ConstructorBots.md) — Bot/automation project; validate its declared runtime and safety tests before joining ecosystem integration.
- [Rankings](./Rankings.md) — Ranking application/library; report build, test, and data-contract evidence.
- [RankingLists](./RankingLists.md) — Ranking/list data project; validate data shape and compatibility tests.
- [DynamoNodes](./DynamoNodes.md) — Revit/Dynamo integration; capture host-specific build prerequisites and separate unavailable-host tests from failures.
- [B4G-Bot](./B4G-Bot.md) — Bot project; surface build/test evidence and any external-service dependencies.
- [B4G_Bot](./B4G_Bot.md) — Bot project variant; establish whether this is distinct from B4G-Bot before declaring compatibility.
- [B4GBot](./B4GBot.md) — Bot project variant; establish whether this is distinct from similarly named repositories.
- [MyCity](./MyCity.md) — City/showcase application; treat demos as presentation evidence, not a substitute for core tests.
- [AmazonBIM](./AmazonBIM.md) — BIM-oriented project; capture domain-specific build and validation evidence.
- [DSD_BIM](./DSD_BIM.md) — BIM project; report build/test evidence and any external model or host requirements.
- [TheSingularityWorkshop](./TheSingularityWorkshop.md) — Workshop umbrella/landing repository; use as ecosystem catalog and link to evidence, not as a proxy for component CI.
- [AnyApp](./AnyApp.md) — Manifest-driven application host; correlate source and package lanes, manifest validation, and publishing variants.
- [FSM_API_Unity](./FSM_API_Unity.md) — Unity-specific FSM API integration; keep Unity dependencies isolated from the dependency-free FSM_API core.
- [FSM_API.ConsoleApp](./FSM_API-ConsoleApp.md) — Console demonstration of FSM_API; validate usage examples against the published/core API.
- [FSM_API](./FSM_API.md) — Core finite-state-machine library; preserve dependency-free runtime and verify unit tests, package contents, and compatibility.
- [TrentBest.github.io](./TrentBest-github-io.md) — Personal/project website; use deployment success and link integrity as evidence, separate from .NET package health.
- [raWWar](./raWWar.md) — Showcase/game-like application; validate real integrated flows and living SVG evidence without treating visuals as core-library tests.
- [DoctorFSM](./DoctorFSM.md) — FSM diagnostic/tooling project; verify diagnostics against real FSM behavior and API versions.
- [DiceFSM](./DiceFSM.md) — FSM sample/game; validate FSM API usage and end-to-end gameplay smoke tests.
- [FSM_UnityIntegrationAdvanced](./FSM_UnityIntegrationAdvanced.md) — Advanced Unity integration; validate Unity-specific adapters independently of core FSM packages.
- [FSM_UnityIntegration](./FSM_UnityIntegration.md) — Unity integration layer; keep engine dependencies at the adapter boundary.
- [Diagrams](./Diagrams.md) — Diagram tooling; verify generated artifacts are deterministic and source-aligned.
- [The](./The.md) — Unclear/placeholder repository; CiAi should not infer a role or include it in dependency compatibility until ownership and purpose are confirmed.
- [FSM_ProjectTemplates](./FSM_ProjectTemplates.md) — Project templates; validate generated project builds from a clean checkout.
- [GitHackforFun](./GitHackforFun.md) — Exploratory project; keep optional and classify only after workflow and purpose audit.
- [FSM_Helix](./FSM_Helix.md) — FSM-related project; establish actual package/runtime role from repository declarations before cross-repo compatibility claims.
- [SimpleDemos](./SimpleDemos.md) — Demo collection; build each maintained sample and distinguish illustrative demos from release gates.
- [MineField](./MineField.md) — FSM/game demo; verify gameplay smoke tests and core API compatibility.
- [FSM_UnityCompatability](./FSM_UnityCompatability.md) — Unity compatibility layer; validate supported Unity/runtime matrix and isolate engine-specific checks.
- [WebPage](./WebPage.md) — Minimal manifest-driven web page; validate source integration, published-package lane, and Azure/deployment evidence separately.
- [FSM_LayerTemplate](./FSM_LayerTemplate.md) — FSM layer template; verify template output compiles and respects package boundaries.
- [FSM_PackageManager](./FSM_PackageManager.md) — Package management tooling; validate package discovery, version resolution, and safe non-publishing tests.
- [FSM_Layer_Serialization](./FSM_Layer_Serialization.md) — FSM serialization layer; test round trips, invalid inputs, and compatibility across versions.
- [FSM_Layer_Generics](./FSM_Layer_Generics.md) — Generic FSM layer; verify type safety and API contract tests.
- [FSM_Layer_DependencyInjection](./FSM_Layer_DependencyInjection.md) — Dependency-injection adapter; validate optional integration without forcing dependencies into core.
- [FSM_Bot](./FSM_Bot.md) — FSM bot project; validate behavior and API contract using deterministic tests.
- [FSM_Bot_Editor](./FSM_Bot_Editor.md) — Editor integration for FSM_Bot; keep editor/host-specific checks separate from runtime tests.
- [FTI_BIM](./FTI_BIM.md) — BIM-related project; collect domain validation and host dependency evidence.
- [TheForge](./TheForge.md) — Workshop/forge showcase; validate its user-facing flows and generated artifacts.
- [ZeroTouchFTI](./ZeroTouchFTI.md) — ZeroTouch/BIM integration; treat Revit/Dynamo host requirements as environment-specific evidence.
- [FaithTechInc](./FaithTechInc.md) — Organization/project site or tooling; define deploy and content checks appropriate to its actual purpose.
- [SingularityWarehouse](./SingularityWarehouse.md) — Warehouse/storage concept; validate data identity, lookup behavior, and persistence contracts if applicable.
- [FSM_Dungeon](./FSM_Dungeon.md) — FSM-powered dungeon/demo; verify game-loop smoke tests and FSM API compatibility.
- [RevitFamilyManagerBuilders](./RevitFamilyManagerBuilders.md) — Revit family-building tools; capture Revit host/version prerequisites and separate skipped host checks.
- [Agents.LLM](./Agents-LLM.md) — LLM agent project; validate provider/configuration boundaries and deterministic contract tests.
- [AnyApp-2026-06-28_19-34-37](./AnyApp-2026-06-28_19-34-37.md) — Historical AnyApp snapshot; use for provenance/reference only unless explicitly reactivated.
- [ShowcasePong](./ShowcasePong.md) — Showcase game; run build and gameplay smoke tests, with visual demo evidence clearly labeled.
- [SingularityShipBuilder](./SingularityShipBuilder.md) — Ship-building showcase; validate manifest-driven content and generated presentation outputs.
- [SingularitySoldiers](./SingularitySoldiers.md) — Soldier simulation/showcase; validate simulation determinism, asset/data inputs, and presentation proof.
- [SingularityBunnies](./SingularityBunnies.md) — Animal/crowd simulation showcase; validate deterministic behavior and performance/scale evidence.
- [SingularityWorkshopAcademy](./SingularityWorkshopAcademy.md) — Learning/training project; verify tutorial steps and sample builds from clean checkout.
- [Workshop](./Workshop.md) — Workshop application/showcase; define build, smoke, and deployment evidence from its actual role.
- [FSM_API_Benchmark](./FSM_API_Benchmark.md) — FSM API benchmark suite; report reproducible benchmark metadata separately from correctness gates.
- [TheSingularityWorkshop.GUI](./TheSingularityWorkshop-GUI.md) — GUI framework; first-class pilot for package/tests, source-derived UML, documentation quality, and generated visual evidence.
- [TheSingularityWorkshop.FSM_COS](./TheSingularityWorkshop-FSM_COS.md) — Composition/compute orchestration; correlate package/source integration lanes and surface actionable job/step failures.
- [TheSingularityWorkshop.FSM_REST](./TheSingularityWorkshop-FSM_REST.md) — REST profiles/API layer; verify build, API contract, package contents, and profile compatibility.
- [TheSingularityWorkshop.FSM_Serialization](./TheSingularityWorkshop-FSM_Serialization.md) — Serialization package; validate format contracts, round trips, malformed input handling, and pack/publish gates.
- [TheSingularityWorkshop.MicroBundleDomain](./TheSingularityWorkshop-MicroBundleDomain.md) — Domain contracts; check schema/API compatibility and downstream consumer matrix.
- [FSM_Serialization_Benchmarks](./FSM_Serialization_Benchmarks.md) — Serialization benchmarks; preserve repeatability and keep performance trends distinct from correctness.
- [MicroBundleDomain_Benchmarks](./MicroBundleDomain_Benchmarks.md) — Domain benchmarks; report environment, input, and comparison metadata; never fail correctness solely on noisy timing.
- [TheSingularityWorkshop.ProtocolAi](./TheSingularityWorkshop-ProtocolAi.md) — Operation vocabulary/policy boundary; validate operation contract versions and policy tests, without giving CiAi publishing authority.
- [TheSingularityWorkshop.GrammarAi](./TheSingularityWorkshop-GrammarAi.md) — Grammar/validation capability; verify parser/grammar contracts and integration tests.
- [TheSingularityWorkshop.MicroBundleRepository](./TheSingularityWorkshop-MicroBundleRepository.md) — Repository/storage implementation; validate manifest resolution, immutable identity/hash behavior, and persistence tests.
- [TheSingularityWorkshop.Experiences](./TheSingularityWorkshop-Experiences.md) — Experience/sample bundles; validate manifests and executable scenario evidence.
- [TheSingularityWorkshop.WebApp](./TheSingularityWorkshop-WebApp.md) — Web AnyApp host; correlate source and package compatibility lanes, UI smoke tests, and deployment evidence.
- [TheSingularityWorkshop.MyVR](./TheSingularityWorkshop-MyVR.md) — VR/visualization host; capture platform-specific builds and keep unavailable device tests distinct from failures.
- [FSM_UserIO](./FSM_UserIO.md) — User interaction/input layer; validate input contracts, event handling, and downstream integration.
- [TheSingularityWorkshop.Ontology](./TheSingularityWorkshop-Ontology.md) — Ontology/model vocabulary; verify identity, schema, and relationship invariants.
- [TheSingularityWorkshop.Renderer](./TheSingularityWorkshop-Renderer.md) — Textual/AI-oriented portrayal; validate renderer-neutral model contracts and FSM_COS composition with/without optional rendering dependencies.
- [TheSingularityWorkshop.Renderer.Benchmarks](./TheSingularityWorkshop-Renderer-Benchmarks.md) — Renderer benchmarks; collect repeatable performance evidence independent of functional correctness.
- [TheSingularityWorkshop.Profiles](./TheSingularityWorkshop-Profiles.md) — Profile/configuration contracts; validate schema compatibility, defaults, and invalid-profile behavior.
- [TheSingularityWorkshop.Economy](./TheSingularityWorkshop-Economy.md) — Economy/domain subsystem; validate model invariants and integration contracts before cross-repo claims.
- [TheSingularityWorkshop.MicroBundleIngestor](./TheSingularityWorkshop-MicroBundleIngestor.md) — Ingestion pipeline; test malformed input, identity/hash verification, and safe rejection behavior.
- [TheSingularityWorkshop.HeadlessAi](./TheSingularityWorkshop-HeadlessAi.md) — Provider-neutral headless AI core; preserve zero-provider coupling and test deterministic core contracts.
- [TheSingularityWorkshop.HeadlessAi.AnyAppExample](./TheSingularityWorkshop-HeadlessAi-AnyAppExample.md) — HeadlessAi/AnyApp example; validate example against both projects without making it a core dependency.
- [TheSingularityWorkshop.HeadlessAi.Benchmarks](./TheSingularityWorkshop-HeadlessAi-Benchmarks.md) — HeadlessAi benchmarks; record repeatable environment and keep benchmark output distinct from correctness.
- [TheSingularityWorkshop.Visualize](./TheSingularityWorkshop-Visualize.md) — Visual portrayal and GPU path; validate semantic-to-visual evidence, observer proofs, and backend-specific capabilities.
- [TheSingularityWorkshop.Gestures](./TheSingularityWorkshop-Gestures.md) — Gesture/behavior capability; validate ontology/seed inputs, deterministic behavior, crowd batching, and scale tests.
- [TheSingularityWorkshop.Physics](./TheSingularityWorkshop-Physics.md) — Physics/math capability; validate numerical tests, dependency/source SHAs, and reproducible parameterized runs.
- [StoryBuilder](./StoryBuilder.md) — Story-building application; validate story contracts, examples, and end-to-end scenario outputs.
- [TheSingularityWorkshop.Foundry](./TheSingularityWorkshop-Foundry.md) — Foundry/build tooling; validate generated outputs, package provenance, and reproducible build steps.
- [TheSingularityWorkshop.CiAi](./TheSingularityWorkshop-CiAi.md) — CiAi itself; validate evidence normalization, correlation, idempotency, read-only policy, and end-to-end request/result flow.
- [TheSingularityWorkshop.CiAi.CogA](./TheSingularityWorkshop-CiAi-CogA.md) — CogA repository catalog; validate repository identities, declarations, capabilities, and relationship integrity.
- [TheSingularityWorkshop.CiAi.CogB](./TheSingularityWorkshop-CiAi-CogB.md) — CogB request proposal; validate typed bounded requests and ensure proposals are not treated as authorization.
- [TheSingularityWorkshop.CiAi.CogC](./TheSingularityWorkshop-CiAi-CogC.md) — CogC result contract; validate correlation IDs, expected versions, and authoritative evidence references.
- [TheSingularityWorkshop.CiAi.CogD](./TheSingularityWorkshop-CiAi-CogD.md) — CogD policy; validate allow/deny behavior and prove package publishing remains denied.
- [TheSingularityWorkshop.CiAi.CogE](./TheSingularityWorkshop-CiAi-CogE.md) — CogE topology projection; validate projections from normalized evidence without inventing or fetching facts.

## Known audit limitations

This first pass is a repository-wide integration design inventory, not a completed workflow-by-workflow audit. Before turning any sample into an enforcement rule, inspect its active branch, actual workflow files, triggers, jobs, permissions, action pins, dependency refs, artifacts, and recent run outcomes. Samples should be updated as those facts are verified.
