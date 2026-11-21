<!-- The Singularity Workshop animated moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# CiAi: Making a Growing Software Ecosystem Understandable to AI

*An evidence boundary between continuous integration, applications, and language models—and a foundation for development that can explain what it knows before it acts.*

A green checkmark is reassuring. But what, exactly, does it prove?

Did the right commit build? Did every required workflow run? Are we looking at the latest attempt, or an earlier retry? Did the tests pass, or did the workflow merely finish? Was a required check skipped? Is the evidence complete—or did the system simply fail to find the rest?

These questions become harder as a software project grows.

Imagine a workshop with dozens of specialized machines. Each machine has its own controls, status lights, operating instructions, and maintenance history. One light means a task started. Another means it finished. A third means the operator inspected the result. If you walk into that workshop and see a row of green lights, you still cannot safely conclude that the whole operation is ready. You need to know what each light means, which machine it belongs to, what job it was running, and whether the evidence is current.

A growing software ecosystem has the same problem. Its machines are repositories. Its work orders are commits. Its activity is distributed across build systems, test suites, workflows, package pipelines, and documentation. Each system can report what happened locally. The difficult part is understanding how all those reports fit together—and knowing what they do *not* authorize.

That is the problem CiAi is being built to solve.

## CI can execute work. Can it explain the evidence?

Continuous integration already does something essential: it builds software, runs tests, and records the results. GitHub Actions, for example, knows which workflow ran, which commit it checked out, which jobs and steps executed, and what conclusion the provider recorded.

But that information is distributed across provider-specific APIs and pages. A human can click around and piece it together. An application can call APIs and write custom glue. An LLM can summarize text. None of those approaches, by itself, guarantees that the summary refers to the right repository, the right commit, the right attempt, or a complete set of required checks.

A language model needs more than a green badge or a paragraph saying “the build passed.” It needs bounded, structured facts with source identity and uncertainty preserved.

CiAi starts with a deliberately narrow idea:

> **Expose continuous integration to AI without turning AI into the authority that runs it.**

The lowercase **i** in CiAi is intentional. CI remains the execution system. CiAi supplies an application-owned boundary through which software and language models can observe, verify, correlate, and understand CI evidence.

GitHub Actions remains authoritative for its workflow execution and reported run state. CiAi does not replace the provider, re-run its jobs, or manufacture a successful result. It reads the provider's evidence, checks that evidence against trusted expectations, and makes the outcome available in stable contracts.

That distinction is the foundation—not a footnote.

## The commit is the anchor

The first question should not be “Did the repository pass?”

It should be:

**What do we know about this exact commit?**

A commit SHA is a content-derived identifier: effectively a fingerprint for one particular source revision. If two workflow runs refer to different SHAs, their results must not be casually combined. A successful run from yesterday does not prove that today's source is healthy. A report for one branch tip does not prove anything about a different commit that happens to have a similar name.

CiAi's observer therefore works from an explicit repository identity and a full commit SHA. It discovers matching GitHub Actions workflow runs, preserves run and attempt identity, and collects job and step evidence using bounded pagination. It can then produce machine-readable JSON and a human-readable Markdown report.

The observer keeps important distinctions visible:

- **Discovery completeness:** did the observer finish discovering the matching runs within its configured bounds?
- **Evidence completeness:** was the required evidence actually found?
- **Terminality:** have the relevant workflows reached a final state?
- **Success:** did the provider report successful completion?

Those are separate facts. A workflow can be finished but unsuccessful. A report can be collected successfully while the build it describes failed. A missing workflow is not a successful workflow. An unknown provider conclusion is not a pass.

This is why CiAi fails closed when required evidence is missing, duplicated, non-terminal, unsuccessful, or incomplete. If the system cannot establish the required facts, it must say so rather than quietly turning uncertainty into success.

~~~mermaid
flowchart LR
    A["Repository + exact commit SHA"] --> B["GitHub Actions"]
    B --> C["Runs and attempts"]
    C --> D["Jobs and steps"]
    D --> E["CiAi observation"]
    E --> F["JSON + Markdown evidence"]
    F --> G["Application or LLM"]
    G -. "reason over evidence" .-> H["Human / host-owned decision"]
    style E fill:#123a39,stroke:#66e0be,color:#edf7ff
    style H fill:#3a2d18,stroke:#ffd27d,color:#edf7ff
~~~

*The provider executes and reports. CiAi observes and verifies. The host and repository owner retain authority over what happens next.*

## A growing ecosystem needs rules, not just status lights

The evidence problem is only half the story. As repositories multiply, so does operational complexity.

A project with a handful of repositories can survive on informal conventions. A workshop with dozens of interconnected repositories cannot. Branches accumulate, dependency relationships become harder to reason about, and an agent can waste time inspecting stale or ambiguous source.

The Workshop's Sith policy addresses one piece of that problem with a simple rule: an active ecosystem repository should have exactly two permanent branches.

| Branch | Responsibility |
|---|---|
| **development** | The active source of truth and normal place for ongoing work and dependency builds |
| **master** | The last released, stable state aligned with published package versions |

The intent is not merely aesthetic. It gives people and automation a predictable answer to two different questions: “What are we working on?” and “What is the released state?”

The release process is correspondingly deliberate: bring the release branch into alignment with development, validate the dependency graph and package candidates, and publish only after the owner explicitly authorizes publication.

CiAi's ecosystem governance should make this kind of state understandable to automation. The Sith policy workflow evaluates branch inventory and reports one of three states:

- **COMPLIANT** — the required branch inventory was verified.
- **NON-COMPLIANT** — the inventory was retrieved and violates the policy.
- **UNVERIFIED** — the system could not establish the inventory.

That third state matters. An API failure or incomplete inventory must not be presented as compliance. The policy workflow reports what it can prove; it does not silently delete branches or take ownership of repository history. Destructive cleanup remains a separate, deliberate action.

The larger lesson is that automation needs both evidence and explicit rules for interpreting it. A status without its policy is just a light on a dashboard.

## Five Cogs, five responsibilities

CiAi is being organized around five small control-plane responsibilities called **Cogs**. They are not five names for one large service, and they are not intended to become a new repository for every feature. Each Cog owns a distinct part of the coordination problem.

| Cog | Responsibility | Plain-language question |
|---|---|---|
| **CogA — Catalog** | Describes repositories, capabilities, versions, and contracts | What is available? |
| **CogD — Policy** | Evaluates whether an operation is permitted for a caller and target | Is this allowed? |
| **CogB — Request** | Creates a bounded, correlated request with pinned inputs | What exactly are we asking for? |
| **CogC — Result** | Correlates and validates the returned result | Does this answer belong to that request? |
| **CogE — Projection** | Presents verified lifecycle facts and diagnostics | What happened, and what do we know now? |

The order above describes the intended decision flow: identify a capability, evaluate policy, form a bounded request, validate the result, and project the resulting facts.

Each Cog has an independently testable implementation slice. The current integration proof pins the Cog sources and exercises their contracts together in-process: catalog lookup, default-deny policy, bounded request creation, result correlation, parent-level artifact and receipt verification, and lifecycle projection.

That is useful proof—but it is important to name its boundary. It demonstrates that the contracts can work together in one process. It does not yet demonstrate a complete remote request/result network, authenticated notification and acknowledgement, or an autonomous agent completing work across repositories.

Keeping that distinction explicit makes the architecture stronger. It tells us what is already reusable and exactly what still needs to be built.

## Evidence is not authority

This is the most important design rule in CiAi:

**Knowing that something happened is not the same as being allowed to make it happen again.**

Consider the different responsibilities involved in an autonomous development system:

| Part | Owns |
|---|---|
| GitHub Actions | Executing workflows and reporting provider-owned run evidence |
| CiAi | Observing, verifying, correlating, and presenting CI facts and admission-contract results |
| ProtocolAi | Stable protocol vocabulary and identity primitives |
| GrammarAi | Structural and grammar validation |
| HeadlessAi | Model invocation |
| FSM_Foundry | Task acceptance, leases, model-turn orchestration, authorization, and handoff |
| Host adapter | Authenticated provider access and concrete dispatch |
| Repository owner | Approval of consequential repository changes and releases |

These boundaries prevent a model-generated sentence, repository instruction, or callback payload from becoming an authorization token.

For example, CiAi now has an **AgentOnboarding admission contract**. It validates a root-level AgentOnboarding.md against the exact commit being evaluated. The validator checks the source identity, document structure, size limit, and content digest, then returns a deterministic eligibility outcome and machine-readable failure reason.

But the onboarding document is still untrusted repository data. It can explain how a repository should be approached; it cannot grant permissions, override host policy, or approve a merge. A passing validation result is necessary for admission, not sufficient for autonomous work.

The host must retrieve the document from the exact commit and report which commit it actually read. Foundry must enforce the admission result before accepting work and before subsequent autonomous turns or mutations. The validator exists; that end-to-end runtime enforcement is still being integrated. CiAi does not currently own a live autonomous-dispatch path.

That is not a weakness to hide. It is a boundary to preserve while the rest of the system is built.

## The first consumer: AnyApp

Architecture becomes much more convincing when another application can use it.

AnyApp is CiAi's first source-based consumer pilot. It is a real .NET desktop application with its own build and test workflow. Rather than asking CiAi to build AnyApp, the integration lets AnyApp's existing GitHub Actions workflow remain the authoritative executor.

For a development commit, the consumer workflow waits for the authoritative .NET workflow to finish for the same full SHA. It resolves the matching run and attempt, checks out CiAi at a pinned immutable source revision, invokes the read-only observer, and uploads JSON and Markdown evidence reports. The token is limited to read-only Actions and contents access.

The first verified observation is especially instructive.

For AnyApp commit **4d40f584fa4011179a731962afdd755fa85e8063**, the CiAi observer successfully collected the evidence from the authoritative AnyApp workflow. The observer's own workflow completed successfully. But the observed AnyApp build/test workflow had failed two integration tests before executable publishing.

CiAi reported that distinction correctly:

- Discovery was complete.
- Required evidence was present.
- The observed workflow was terminal.
- The observed workflow was **not successful**.
- The required-workflow evidence state was **failed**, not passed.

This is the point of the exercise. The evidence collector can succeed at its job while accurately reporting that the build it inspected failed.

A less careful integration might show a green check beside the observer workflow and leave a reader with the wrong impression. CiAi preserves the identity of the thing being observed, so the success of the observer cannot be mistaken for the success of the application build.

That is a small but meaningful step toward software that can explain its own development history.

## A living picture, not just a document

The best way to understand a system like CiAi is to see its evidence move through the boundary.

The [interactive CiAi experience](../../index.html) is the companion demonstration. It introduces the provider-to-verification flow and lets a visitor explore complete-evidence and missing-evidence scenarios. Its browser walkthrough uses explicitly labeled illustrative data; it does not connect to GitHub or claim to display a live CI result.

The distinction between the demonstration and the implementation is intentional:

- The **interactive page** teaches the concepts.
- The **source and tests** define the behavior.
- The **GitHub Actions runs and artifacts** provide execution evidence.
- The **consumer integration** shows how another repository can use the observer.

A useful next evolution is to connect more of the living picture to source-derived facts: repository topology, exact dependency revisions, branch-policy states, workflow evidence, and the lifecycle state of cross-repository requests. The visual should never invent a green light. It should show verified, missing, contradictory, and unknown states as different things.

That is what makes a living picture more than decoration: it is a view of the same facts the system reasons over.

## What exists now—and what comes next

CiAi is a working set of evidence and coordination building blocks, not yet a finished autonomous development platform.

**Implemented and exercised today:**

- Read-only GitHub Actions observation by repository and exact full commit SHA.
- Matching workflow-run discovery with attempt identity and bounded job/step collection.
- JSON and Markdown evidence reports with fail-closed handling of incomplete required evidence.
- Provider-neutral run snapshots and exact identity reconciliation.
- Typed artifact, ready-notice, receipt, and payload-integrity contracts, including bounded stream reading and a local durable receipt ledger.
- Standalone CogA–CogE contract slices and a pinned in-process vertical integration proof.
- Exact-commit AgentOnboarding document validation.
- An AnyApp consumer pilot that demonstrates source-pinned, read-only evidence collection against a real application workflow.

**Still being built or deliberately left to host systems:**

- A complete authenticated remote request/result lifecycle.
- Provider-side artifact retrieval and end-to-end authoritative provider correlation for remote handoffs.
- Durable cross-repository notification, acknowledgement, replay, and idempotency behavior.
- Foundry runtime enforcement of onboarding admission and task authorization.
- Live HeadlessAi-driven autonomous dispatch through a host adapter.
- A complete, end-to-end CI policy system that connects repository-defined checks to provider-hosted execution.
- Any CiAi-controlled publication or deployment authority.

The current package candidate is **TheSingularityWorkshop.CiAi 0.1.0-alpha.1**, targeting .NET 8 and carrying no external dependencies in its core library. Its build, contract checks, package creation, and isolated package-consumer validation have been exercised in CI. That makes it a candidate—not a published package. Publication remains a separate owner decision and has not been authorized by a passing workflow.

This separation is deliberate. An evidence report can inform a decision. A policy evaluator can say whether a bounded operation is eligible. A model can propose a change. None of those facts should silently become permission to merge, publish, deploy, or delete.

## The larger idea

A software ecosystem should not require an AI to guess how it works.

Each repository should be able to expose what it provides, what it consumes, which source revision is under discussion, what evidence supports its current state, and which actions remain outside the agent's authority. CiAi is one part of making that possible: a common evidence boundary that lets applications and models reason about development without confusing observation with control.

The goal is not to give an LLM a bigger button.

The goal is to give it a better understanding of the system before anyone decides whether it should press one.

**Let CI execute. Let CiAi explain the evidence. Let the host enforce policy. Keep consequential authority with the owner.**

---

## Explore the implementation

- [Interactive CiAi experience](../../index.html)
- [CiAi source repository](https://github.com/TrentBest/TheSingularityWorkshop.CiAi)
- [Implementation overview](../../README.md)
- [CiAi gallery and proof links](../../Gallery.md)
- [Cogs and capability providers](../COGS_VS_CAPABILITIES.md)
- [AgentOnboarding admission contract](../AGENT_ONBOARDING_ADMISSION_V1.md)
- [AnyApp consumer walkthrough](../Samples/AnyApp.md)
- [Release readiness and publication boundary](../RELEASE_READINESS.md)

---

<!-- THE SINGULARITY WORKSHOP FOOTER -->
<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop" width="200" /></p>
