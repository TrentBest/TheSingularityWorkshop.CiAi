<!-- The Singularity Workshop moniker -->
<p align="center">
  <a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a>
</p>

# My CI Was Getting Cumbersome so I created a new Nuget Package to make life easier!

*I built CiAi because my own continuous integration was getting cumbersome—and I wanted a reusable NuGet package that would make the evidence easier to understand.*

The name is deliberately simple. **ci-ai** is the philosophy: *CI exposed to AI*. **TheSingularityWorkshopCiAi** is the NuGet package identifier. The lower-case phrase describes the boundary and the direction of information; the package name identifies the software I built to put that idea into practice.

This is not a story about discovering an AI package or handing my CI system over to an AI. I built the package to solve a problem in my own engineering workflow: make the facts CI already produces easier for people and AI assistants to inspect, without giving the assistant authority over execution or release decisions.

The Singularity Workshop is a growing line of .NET packages and applications. Their CI has to do more than compile one project: it can build and test code, check public-package compatibility, integrate sibling repositories, run security analysis, and preserve evidence for a particular candidate commit. The more capable the ecosystem became, the more cumbersome the question became:

**What, exactly, did CI prove about this exact commit?**

A green badge is useful. It is not the whole story. Which SHA did the run test? Did every relevant workflow finish? Was a lane skipped, still running, or successful? Did a test exercise the local source graph or the published package a consumer will actually install? Where is the evidence behind the summary?

While working through those questions, I built the pattern into a reusable capability—not just a pile of scripts for my own repositories. That became **CiAi**, another package in The Singularity Workshop's deliberately named family.

The goal isn't to put an AI in charge of CI. It's to make CI's real evidence structured enough that a person or an AI assistant can inspect it without guessing.

## One family, deliberate boundaries

The “Ai” names are not a promise that an AI gets unrestricted control. They identify boundaries between systems and the information that crosses them:

- **ProtocolAi** establishes a protocol-facing boundary: what is communicated and how that interaction is represented.
- **GrammarAi** establishes a grammar-facing boundary: how structured language or input is described and handled.
- **HeadlessAi** keeps headless AI capability behind a provider-neutral boundary, rather than coupling the core to a particular provider.
- **CiAi** establishes the CI-evidence boundary: it organizes what the CI provider reports so an AI assistant can inspect and explain it.

Each package has its own responsibility. The shared principle is controlled exposure: make the relevant data and capabilities available across a boundary without confusing that access with ownership or authority. In CiAi's case, GitHub Actions remains responsible for running CI, and I remain responsible for deciding whether to release.

## Before: the evidence was there, but I was assembling the story

This is a deliberately sanitized, conceptual illustration—not a dump of private workflow configuration, repository-specific failure details, secrets, tokens, or internal logs.

<p align="center">
  <img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.CiAi/development/docs/assets/coderlegion-ci-before.svg" alt="Sanitized conceptual illustration of CI evidence scattered across separate workflows before CiAi" width="100%" />
</p>

Each workflow can tell the truth about its own run and still leave a bigger coordination job for the developer. I was opening runs, comparing commit identities, checking job and step outcomes, and trying to keep the differences between “passed,” “skipped,” “still running,” and “not observed” straight.

That manual reconciliation is where context gets lost. An assistant can help explain evidence—but if the evidence is scattered or incomplete, it can also confidently explain the wrong thing. I wanted a repeatable contract between the CI provider and the AI-facing layer.

## The shift: keep GitHub Actions in charge; let CiAi organize the evidence

<p align="center">
  <img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.CiAi/development/docs/assets/coderlegion-ci-after.svg" alt="Sanitized conceptual illustration of GitHub Actions feeding exact-commit evidence to CiAi for a structured report, while the owner retains release authority" width="100%" />
</p>

CiAi does **not** replace GitHub Actions, run the build instead, or decide that a release should happen. GitHub Actions executes the work and remains authoritative for the results it reports. CiAi observes those results, validates identity, normalizes provider facts, and produces a versioned report that people and AI tools can read.

In this first integration, the report contract is `ciai.commit-observation.v1`. It records the repository and exact head SHA, observed workflow runs, attempt identities, workflow paths, job outcomes, step details, evidence URLs, and discovery coverage. The point is not to make the report look clever. The point is to make its claims traceable.

The boundary is intentional:

- **GitHub Actions executes.** It owns the build, tests, and security workflow.
- **CiAi observes and explains.** It gathers provider-reported evidence and checks that the report refers to the expected repository and commit.
- **The owner authorizes releases.** A report is not permission to publish, deploy, merge, or mutate a repository.
- **An AI assistant reasons from the report.** It can explain, compare, and point out gaps; it must not turn a failed result into success or invent missing evidence.

## The first tangible proof: CiAi observes FSM_COS CI

The first pilot demonstrated the idea against FSM_COS, and I am now using CiAi as part of the evidence workflow around the FSM_COS alpha.7 release candidate. For this release gate, the candidate is pinned to one exact commit rather than described vaguely as “the latest build.”

**Current alpha.7 candidate:** `3c0ca60a0baae665b8871b2a2aacacf8827976c9`

- [FSM_COS Pack and publish — run 40067625575](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS/actions/runs/40067625575)
- [CodeQL — run 40067625528](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS/actions/runs/40067625528)
- [FSM_COS candidate commit](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS/commit/3c0ca60a0baae665b8871b2a2aacacf8827976c9)
- [CiAi observer workflow](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/workflows/observe-release-candidate.yml)
- [CiAi source repository](https://github.com/TrentBest/TheSingularityWorkshop.CiAi)

For the current candidate, the FSM_COS package workflow completed successfully, including build/test, public-package compatibility, source integration, and package inspection; CodeQL also completed successfully on the same SHA. The NuGet publication job was skipped, so these checks are evidence for the candidate—not evidence that the package was published.

The observer workflow is a separate, manually dispatched step. Its registration is present on CiAi's default branch, but a current candidate observation artifact should only be claimed after that workflow has actually been run and its artifact inspected. That distinction matters: **a passing CI run, a successful observation, a completed release gate, and an actual publication are four different things.** The observer is advisory evidence, not yet a required merge gate.

That limitation is exactly why I want a contract and a report rather than a magic green/red answer. A trustworthy tool needs to preserve what it knows—and what it does not know.

## The UML matters: make the boundary visible

I've been building source-grounded UML and living SVG documentation across the Workshop. That same habit matters here: CiAi shouldn't be a mysterious AI-shaped box. Its responsibilities and hand-offs should be inspectable.

### CiAi's read-only observation path

The existing [CiRunObservation UML class card and use case](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/UML/CiRunObservationUML.md) describes the core path: validate repository identity, read a GitHub Actions run, normalize the provider response into a snapshot, and preserve the exact evidence URL. The [CiRunEvidenceVerifier UML](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/UML/CiRunEvidenceVerifierUML.md) documents the next boundary: compare observed facts against trusted expectations for repository, run ID, and SHA.

```mermaid
classDiagram
    direction TB
    class GitHubActionsRunClient {
        +GetRunAsync(repository, runId) CiRunSnapshot
    }
    class RepositoryIdentity {
        +IsValid(repository) bool
    }
    class CiRunSnapshot {
        +Repository
        +RunId
        +HeadSha
        +Status
        +Conclusion
        +EvidenceUrl
    }
    class CiRunEvidenceVerifier {
        +VerifySuccessfulRun(snapshot, expectedRepository, expectedRunId, expectedHeadSha)
    }
    GitHubActionsRunClient ..> RepositoryIdentity : validates identity
    GitHubActionsRunClient --> CiRunSnapshot : normalizes provider facts
    CiRunEvidenceVerifier --> CiRunSnapshot : reconciles evidence
```

### The larger Workshop uses visual boundaries too

These are existing SVG UML diagrams from FSM_COS, the composition layer being exercised by this pilot. They show the same documentation philosophy: make the boundary and responsibility legible, not just the implementation name.

<p align="center">
  <img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.FSM_COS/development/docs/UML/ClassDiagram/FsmCosUML.svg" alt="Existing FSM_COS UML SVG showing the composition entry point and consumer boundary" width="100%" />
</p>

<p align="center">
  <img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.FSM_COS/development/docs/UML/ClassDiagram/RuntimeManifestScheduleUML.svg" alt="Existing FSM_COS UML SVG showing dependency readiness and scheduling boundaries" width="100%" />
</p>

Those two diagrams are about FSM_COS, not a claim that they depict CiAi internals. The CiAi class cards above document the observer itself; the FSM_COS diagrams place the integration in the broader architecture. And because these visuals are SVGs served from the repositories, the documentation can evolve with the code instead of becoming a stale screenshot. The animated Workshop moniker at the top is part of that same idea: documentation can be alive.

## What I want CiAi to become

This is the first real integration, not a claim that every Workshop repository is already using CiAi or that release decisions are automated. The next steps are to run and inspect the observer for the current candidate, capture final post-run state, reconcile multiple workflow types for the same candidate, test missing and incomplete evidence paths, and expand the pilot carefully.

The wider opportunity is straightforward: every repository that produces CI evidence shouldn't need to invent its own AI-facing explanation format. A small, reusable NuGet package can help make those facts consistent and consumable—while leaving execution authority with the CI provider and release authority with the owner.

That's the moment for me: I had a practical annoyance in my own engineering workflow, so I built a reusable NuGet package around it. CiAi gives AI a well-defined view of CI evidence without making AI the source of truth. The same boundary-first thinking runs through ProtocolAi, GrammarAi, and HeadlessAi: expose the right information for a specific job, keep responsibilities clear, and don't confuse assistance with authority.

> **Let CI execute. Let CiAi explain the evidence. Keep release authority with the owner.**

## Follow the work

- [CiAi repository and README](https://github.com/TrentBest/TheSingularityWorkshop.CiAi)
- [CiAi UML: read-only observation](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/UML/CiRunObservationUML.md)
- [CiAi UML: evidence verifier](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/UML/CiRunEvidenceVerifierUML.md)
- [FSM_COS integration workflow](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS/actions/runs/40071136587)
- [CiAi ecosystem CI integration roadmap](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/ECOSYSTEM_CI_INTEGRATION_ROADMAP.md)

---

## Editorial status

**Draft for CoderLegion — announcement / attention-getter.** The before/after illustrations are intentionally sanitized conceptual diagrams. Re-check live run states and the SVG embeds immediately before publication. The alpha.7 CI and CodeQL runs are complete on the named candidate; the separate observer still needs to be dispatched and its artifact reviewed before claiming a current observation report. This is not a claim of ecosystem-wide rollout or AI-controlled publishing.

<!-- THE SINGULARITY WORKSHOP FOOTER -->
<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop avatar" width="200" /></p>
