<!-- The Singularity Workshop moniker -->
<p align="center">
  <a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop — animated workshop moniker" width="720" /></a>
</p>

# I Didn't Replace GitHub Actions. I Taught My CI to Explain Itself.

*An early, evidence-backed look at CiAi: a read-only bridge between continuous integration and AI.*

I have a growing ecosystem of .NET projects at The Singularity Workshop. They depend on one another, build against both published packages and sibling repositories, generate documentation visuals, run tests, inspect package contents, and perform security analysis. A green check is useful—but as the ecosystem grows, I need more than a green check. I need to know **which commit was tested, which workflow ran, which jobs and steps actually completed, and what evidence is missing**.

That is the problem CiAi is being built to solve.

CiAi is short for “CI exposed to AI.” It is not an AI-powered CI runner, and it does not replace GitHub Actions. GitHub Actions remains the authority that executes builds and tests. CiAi reads and normalizes the evidence those workflows produce, so a human or an AI assistant can reason about the facts without inventing them.

## The first real integration: FSM_COS

On October 24, 2026, I added an advisory CiAi observation job to the [FSM_COS Pack and publish workflow](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS/actions/runs/40071136587).

The integration is pinned to a specific CiAi source commit, builds the observer in the workflow, and queries GitHub Actions for the exact FSM_COS commit SHA. It validates that the report names the expected repository and commit, checks that the observation is complete, and uploads the JSON and Markdown reports as a workflow artifact.

The first integrated candidate was:

- **Repository:** [TheSingularityWorkshop.FSM_COS](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS)
- **Commit:** `56ad34dbe4722ba1edf9a4da6a00812eeaa29101`
- **Pack and publish workflow:** [Run 40071136587](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS/actions/runs/40071136587)
- **CodeQL:** [Run 40071136371](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS/actions/runs/40071136371)
- **CiAi observation artifact:** [Workflow artifacts](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS/actions/runs/40071136587)

The build-and-test, public-package compatibility, and full source-integration lanes passed. CodeQL passed on the same commit. The CiAi observer built, ran, validated the report identity, and uploaded the artifact successfully. The overall Pack and publish workflow completed successfully, while its NuGet publication job was skipped.

That last detail matters: **passing CI is not permission to publish**. The NuGet publishing guard remains disabled. CiAi has read-only observation permissions for this pilot; it does not publish a package, alter branches, or turn a model-generated sentence into release authorization.

## What the report actually says

The observer emits a versioned report, `ciai.commit-observation.v1`, containing the repository and exact head SHA, discovery coverage, workflow run identities, attempt numbers, workflow paths, job outcomes, step details, and links back to GitHub's evidence.

The report is intentionally honest about its limits. The first artifact was captured while its parent Pack and publish workflow was still running, so it recorded that workflow as in progress at observation time. The parent workflow later completed successfully. This is a useful distinction: a complete *observation* means the collector retrieved a complete set of records under its contract; it does not mean every observed workflow had already finished, every check passed, or the package was release-ready.

CiAi must preserve the difference between:

- a successful build and a successful public-package compatibility test;
- source-graph integration and compatibility against published packages;
- a completed workflow and a workflow that is still running;
- a skipped step and a passing step;
- complete evidence and missing or truncated evidence;
- a green CI result and an authorized release.

Those distinctions sound small until you are coordinating a set of interdependent repositories. Then they become the difference between a trustworthy release record and a misleading summary.

## Why keep it read-only?

Because the first job is to establish trust in the evidence.

The provider owns the execution facts. CiAi validates and normalizes them. An LLM can help explain the report, compare evidence, identify gaps, or suggest the next diagnostic step—but it should not be allowed to rewrite a failed conclusion as success or grant itself permission to publish.

The current integration is therefore advisory. It runs after FSM_COS's build/test, public-package compatibility, and source-integration lanes, including when one of those lanes fails. Its report is an artifact for inspection, not yet a required merge gate. That gives me a chance to test missing runs, skipped jobs, stale evidence, incomplete pagination, and provider errors before I trust the report as a release control.

## The direction

The first milestone is modest on purpose: observe the exact commit and preserve the evidence. The next milestones are to verify final post-run state, reconcile multiple workflow types on the same candidate, distinguish source builds from public-package checks, and then pilot CiAi across more Workshop repositories.

I want the result to be a CI system that is easier for both people and AI assistants to understand—not because an AI says “looks good,” but because the underlying evidence is explicit, traceable, and bounded.

The principle is simple:

> **Let CI execute. Let CiAi explain the evidence. Keep release authority with the owner.**

CiAi is still early, and this is the first working integration—not a claim that the entire Workshop ecosystem is now governed by it. But it has crossed an important line: the FSM_COS workflow now builds and runs the pinned CiAi observer against its own exact commit and retains the resulting report.

That's the kind of AI-assisted engineering I want to build: useful, inspectable, and honest about what it knows.

## Follow the work

- [CiAi source and README](https://github.com/TrentBest/TheSingularityWorkshop.CiAi)
- [FSM_COS integration run](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS/actions/runs/40071136587)
- [CiAi ecosystem CI integration roadmap](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/ECOSYSTEM_CI_INTEGRATION_ROADMAP.md)

---

## Editorial status

**Draft for CoderLegion.** Evidence links and wording should be reviewed again immediately before publication. This article describes the first advisory integration, not a required release gate or an AI-controlled publishing system.

<!-- THE SINGULARITY WORKSHOP FOOTER -->
<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop avatar" width="200" /></p>
