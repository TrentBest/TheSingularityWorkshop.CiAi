# CiAi Product Assessment: Domain Coverage, Complexity-First Design, and Fluent Transitions

**Status:** Living product-discovery assessment  
**Branch:** `development`  
**Purpose:** Preserve the reasoning behind CiAi's target workflow families, the evidence used to prioritize them, and the method for moving from difficult CI/release scenarios toward simpler ones without weakening safety.  
**Evidence standard:** Separate observed repository facts, design hypotheses, and validated product decisions. Do not present this initial assessment as a statistically representative market study.

## 1. Executive thesis

CiAi should be designed from the domain of real CI/CD and release-engineering work, not from the syntax of a particular workflow provider.

The working thesis is that the strongest product opportunity lies in expressing complicated delivery intent in a form that is:

- **Fluent:** communicates desired outcomes and policy rather than incidental orchestration syntax.
- **Structured:** makes dependencies, state, conditions, evidence, failure handling, and side effects explicit.
- **Composable:** lets a small set of understandable primitives express both common checks and large release processes.
- **Auditable:** explains what was requested, observed, evaluated, authorized, executed, and verified.
- **Safe by construction:** never confuses a green workflow or a successful evaluation with permission to publish or deploy.
- **Provider-neutral at the domain layer:** avoids making the core product synonymous with GitHub Actions, while allowing provider-specific adapters to observe or execute concrete operations.

These are product hypotheses to test, not claims that market demand or implementation completeness has already been proven.

## 2. The design method: hardest useful case first

The proposed method follows a complexity-first domain analysis:

1. **Map the domain.** Enumerate major workflow families and the outcomes they exist to produce.
2. **Find the difficult expressions.** Prioritize workflows with many dependencies, trust boundaries, partial-failure states, artifact identity requirements, and consequential side effects.
3. **Study real examples.** Inspect actual workflow definitions and supporting scripts from our repositories and a deliberately varied set of public projects. Record repository, path, revision, date inspected, and what the example actually demonstrates.
4. **Separate intent from mechanism.** Identify what the team means to guarantee versus how its current YAML/scripts attempt to achieve it.
5. **Extract invariants.** Record what must remain true across success, failure, retries, cancellation, concurrency, and partial completion.
6. **Find reusable primitives.** Model the smallest domain concepts that cover several unrelated examples without erasing important differences.
7. **Express the complex case fluently.** Draft an illustrative API that makes the difficult workflow understandable end-to-end.
8. **Walk down the complexity gradient.** Simplify the same model for narrower cases; do not create a separate conceptual system for every tier.
9. **Test transitions and omissions.** Verify that simplification does not silently remove safety, evidence, error states, or authorization requirements.
10. **Validate against examples not used to design it.** Hold back samples as a test set so the API is not merely fitted to the first few repositories.
11. **Revise the priority order from evidence.** Track complexity, likely reach, risk reduction, reuse, implementation cost, and evidence confidence separately.
12. **Only then stabilize the public API.** Examples should drive the design; they must not be mistaken for proof that the implementation already supports the proposed behavior.

This is an iterative loop, not a one-time requirements exercise.

## 3. What “complexity” means for CiAi

A long workflow is not automatically a hard workflow. Assess complexity along independent dimensions:

| Dimension | Questions |
|---|---|
| Dependency structure | Are there dependent jobs, component graphs, release trains, or conditional branches? |
| State and lifecycle | Are there candidates, approvals, promotions, deployments, rollbacks, or resumable stages? |
| Trust boundaries | Which credentials, providers, repositories, environments, and external systems are involved? |
| Artifact identity | Can the exact tested bytes be bound to source, build, version, digest, and destination? |
| Evidence quality | Are required checks and artifacts actually present, fresh, complete, and tied to this revision/run? |
| Failure ambiguity | Can skipped, cancelled, timed-out, partially completed, and unknown outcomes be distinguished? |
| Side-effect consequence | Could a retry publish twice, migrate data, deploy to production, or irreversibly change external state? |
| Concurrency and idempotency | What happens when runs overlap, events repeat, or a worker resumes after interruption? |
| Policy complexity | Are there environment-specific gates, approvals, exceptions, or policy revisions? |
| Portability | Which semantics are universal and which depend on the provider? |
| Observability and audit | Can a person reconstruct why the system made a decision and what happened next? |

Use these dimensions rather than a single unexamined “complexity score.” If we later create a score, document the weights and keep the underlying ratings visible.

## 4. Initial target ordering: highest consequence toward simplest

This order is an initial design hypothesis. It prioritizes the cases most likely to expose architectural weaknesses, not a measured ranking of market share.

### Tier 1 — High-consequence, cross-stage workflows

1. **Governed release orchestration:** source validation, build/test/security, immutable candidate, evidence evaluation, approval, publication, deployment, post-deployment verification, and controlled recovery.
2. **Monorepo and multi-repository release trains:** affected-component discovery, dependency closure, compatible version sets, ordered/parallel validation, coordinated releases, and partial-completion handling.
3. **Artifact provenance and supply-chain integrity:** archive inspection, digests, symbols, SBOMs, attestations, signatures, source binding, and immutable evidence retention.
4. **Security-gated delivery:** secret/dependency/code scanning, vulnerability policy, exception scope and expiry, trusted build boundaries, and protection against untrusted pull-request code obtaining privileged credentials.
5. **Deployment and data migration:** environment promotion, compatibility windows, migration ordering, health checks, rollback or roll-forward decisions, and irreversible-action handling.
6. **Cross-platform and multi-runtime matrices:** dimensions, exclusions, required versus advisory combinations, aggregation, and precise reporting of incomplete matrices.
7. **Generated code, schemas, and reproducible outputs:** generation drift, toolchain identity, determinism, source maps, and consistency between generated artifacts and committed sources.
8. **Dynamic test selection and quality policy:** affected-code analysis, dependency-based test selection, coverage thresholds, flaky-test policy, and the risk of accidentally skipping required tests.

### Tier 2 — Reusable orchestration patterns

9. Build once and promote the exact same artifact through stages.
10. Release candidate construction and package verification.
11. Dependency update automation with compatibility and security checks.
12. Cache-aware builds with correct invalidation and isolation.
13. Parallel job graphs, resource limits, concurrency groups, and cancellation policy.
14. Flaky tests, bounded retries, timeouts, quarantine, and clear terminal outcomes.
15. Scheduled, manual, and event-driven workflows with deduplication and eligibility rules.
16. Run diagnosis, evidence collection, and explanation of missing or contradictory checks.

### Tier 3 — Narrow, high-frequency building blocks

17. Pull-request validation.
18. Main-branch build and test.
19. Formatting, linting, and static analysis.
20. Unit tests and coverage reporting.
21. Documentation, generated-file drift, and repository hygiene checks.
22. Package/build smoke checks and simple artifact inventories.

These tiers should be rearranged if representative workflow sampling demonstrates a better target order. Tier placement does not imply implementation support.

## 5. The unifying lifecycle model

The same conceptual model should cover all tiers:

**Intent → Eligibility → Plan → Observation → Evidence → Evaluation → Authorization → Execution → Verification → Audit**

Each boundary has a different meaning:

- **Intent:** the desired outcome and declared constraints.
- **Eligibility:** whether the event/revision is allowed to start this recipe.
- **Plan:** a resolved graph of steps, dependencies, conditions, matrices, and failure policy.
- **Observation:** facts reported by a provider or runner; observations may be incomplete or stale.
- **Evidence:** artifacts and records that support or contradict a claim.
- **Evaluation:** a reasoned result such as pass, fail, or indeterminate against a named policy revision.
- **Authorization:** a trusted decision scoped to the action, artifact, revision, and destination.
- **Execution:** the requested side effect performed by a suitable adapter.
- **Verification:** confirmation of the actual external result, not merely a successful request.
- **Audit:** a durable account linking recipe, source, run, evidence, decisions, authorization, actions, and results.

**Required distinction:** observed is not evaluated; evaluated is not authorized; authorized is not executed; executed is not verified. A missing, skipped, cancelled, timed-out, failed, stale, contradictory, or unknown result must not be silently treated as success.

A successful definition build or a fluent recipe must not authorize publication. A green run for another source revision must not satisfy evidence requirements for this revision.

## 6. State-machine thinking: explicit states and guarded transitions

CiAi should use state-machine reasoning where lifecycle semantics demand it, without forcing every ordinary build step to be exposed as a public FSM object.

For consequential workflows, model at least:

- Named states and the facts that establish entry into them.
- Allowed transitions and explicit guards.
- Evidence requirements for each guarded transition.
- Actions/side effects triggered by transitions.
- Retry, timeout, cancellation, and compensation semantics.
- Idempotency and duplicate-event handling.
- Terminal states that distinguish success, failure, cancellation, indeterminate outcome, and partial completion.
- Correlation identities for source revision, workflow run/attempt, candidate artifact, policy revision, approval, and destination.
- Recovery behavior when an external operation may have succeeded but its response was lost.

Illustrative lifecycle:

`Proposed → Validated → CandidateBuilt → EvidenceEvaluated → AwaitingAuthorization → Authorized → Published → DeploymentVerified`

This is only an example, not a claim about current implementation. Each arrow must have explicit conditions; some transitions must be unavailable if evidence is missing or identity has changed. Failure and recovery paths must be designed alongside the happy path, not appended afterward.

The fluent API should describe intent and constraints. A plan compiler/validator should expose the resulting graph and its risks. Provider adapters should report observations or perform only the operations they are permitted to perform. Policy and authorization must remain explicit.

## 7. How we choose what to support first

For every candidate workflow family, keep a decision record with these fields:

- **User outcome:** what the team is trying to achieve.
- **Real examples:** linked source files, immutable revisions, and sample provenance.
- **Reach hypothesis:** which kinds of projects or teams might need it; label this as a hypothesis unless measured.
- **Complexity profile:** ratings for the dimensions in Section 3.
- **Risk addressed:** what could go wrong and how the model prevents or detects it.
- **Common structure:** invariants shared across examples.
- **Meaningful variation:** differences that must remain expressible.
- **Candidate fluent expression:** illustrative API, clearly labeled proposed.
- **Required evidence:** inputs and outputs needed to substantiate the result.
- **State and failure model:** normal, failure, retry, cancellation, timeout, and partial-completion paths.
- **Provider-specific assumptions:** details that belong in adapters rather than the domain model.
- **Implementation gap:** what is already implemented, partially implemented, or not implemented.
- **Test plan:** contract, edge, abuse, integration, and cross-provider cases.
- **Confidence and unknowns:** what remains unverified.
- **Decision:** implement now, prototype, defer, or reject—with rationale.
- **Revisit trigger:** what new evidence would change the decision.

### Prioritization principle

Prefer primitives that cover several independently observed workflow families and reduce consequential ambiguity. Do not choose a feature solely because it is impressive, syntactically elegant, or easy to demo. Equally, do not overbuild the entire release platform before proving that the domain model makes workflows easier to express and inspect.

A useful early wedge to test is: **make complex CI/release workflows understandable and verifiable before they surprise the team.** This could create value before CiAi has authority to execute releases.

## 8. Workflow-sampling protocol

The current catalog is a hypothesis map. Ground it through a deliberately varied sample.

### Sample sources

1. **Our own repositories:** include simple library CI, package validation, documentation generation, cross-repository integration, and release-oriented workflows where present.
2. **Public repositories:** choose examples from distinct project types and maturity levels, including a straightforward library, a multi-platform project, a monorepo, a container/service deployment, and a project with security or provenance controls.
3. **Different providers where practical:** do not assume GitHub Actions is the entire domain.
4. **Counterexamples:** include simple workflows that are already clear and complex-looking workflows whose complexity is accidental or unnecessary.

### Sampling rules

- Record repository, workflow path, exact commit SHA, URL, and inspection date.
- Inspect workflow files and relevant called scripts/actions when needed; YAML alone may hide the actual behavior.
- Record what is directly visible separately from inferred intent.
- Do not infer organization-wide practice from one workflow.
- Do not use raw workflow count as a proxy for market size.
- Keep a held-out set for validating the emerging API.
- Record inaccessible files, missing scripts, and unresolved semantics as unknown—not as proof that a behavior is absent.
- Prefer examples that add a new domain pattern over many near-duplicates.
- Update this document when evidence materially changes priorities.

### Initial observations (small, non-statistical sample)

**A. CiAi — repository CI workflow**

Source: [`.github/workflows/ci.yml`](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/.github/workflows/ci.yml)  
Revision: file blob `3772eb7a0ceae345490d43f915f686adccc8688e` on `development` at time of inspection.

This file is part of CiAi's own CI and provides a baseline sample. The next audit pass should inspect the complete workflow and any invoked scripts before recording exact trigger, job, permission, and failure semantics. No detailed behavior is inferred here from the filename alone.

**B. CiAi — release-candidate observation workflow**

Source: [`.github/workflows/observe-release-candidate.yml`](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/.github/workflows/observe-release-candidate.yml)  
Revision: file blob `3027d68e50df62db2f2b3350d32903ba7c5ea014` on `development` at time of inspection.

Its presence identifies a distinct observation-oriented workflow family worth auditing: collect provider facts about a candidate without confusing observation with release authorization. Exact semantics, permissions, and outputs require full content review before stronger claims are recorded.

**C. FSM_API — .NET library CI workflow**

Source: [`.github/workflows/dotnet.yml`](https://github.com/TrentBest/FSM_API/blob/master/.github/workflows/dotnet.yml)  
Revision: file blob `53394a12cf6eb5cf977a84449e6b635e62a29590` on `master` at time of inspection.

This is an example from a different repository and a conventional .NET library context. It can help test whether primitives derived from high-consequence orchestration simplify to ordinary build/test/package checks. Its precise behavior must be extracted from the file rather than assumed from its name.

**Sampling limitation:** these initial entries establish candidate artifacts for analysis, not validated conclusions about their complete semantics. This sample is too small and too concentrated in our own ecosystem to support prevalence or market-share claims. The next revision should add completed, line-by-line workflow analyses and public-project examples.

## 9. From examples to a fluent model

For each sampled workflow, create three parallel representations:

1. **As observed:** what the workflow, scripts, permissions, and provider actually specify.
2. **As intended:** the user-facing outcome, policies, and invariants inferred or confirmed from documentation/maintainer intent.
3. **As proposed in CiAi:** a provider-neutral expression and its compiled plan, with gaps made explicit.

Compare them for:
- Concepts that recur across unrelated workflows.
- Provider syntax that leaks into the domain unnecessarily.
- Hidden assumptions and implicit defaults.
- Repeated boilerplate that could become a safe default.
- Important differences that must not be flattened into a generic step.
- Safety checks that are easy to omit in YAML.
- Places where a fluent API could create false confidence by hiding behavior.

Do not publish a proposed API as supported functionality until a corresponding implementation and tests exist.

## 10. Coverage and evidence dashboard

Maintain two separate measures:

**Domain coverage**
- Number of sampled workflow families.
- Number of distinct examples and providers.
- Number of examples represented by current primitives without loss of required semantics.
- Number of unresolved/unsupported patterns.
- Number of held-out examples passed.
- Confidence in each conclusion.

**Implementation readiness**
- Implemented and tested primitives.
- Contract and edge-case coverage.
- Provider adapters and permissions reviewed.
- Evaluation semantics and indeterminate outcomes.
- Authorization enforcement.
- Execution, idempotency, and post-action verification.
- Audit/evidence retention.
- Documentation, package validation, and release safeguards.

Do not collapse these into a single percentage. A wide catalog can coexist with a small implementation; a feature-complete prototype can still have weak domain coverage.

## 11. Current implementation boundary (as last verified)

At the time this assessment was written, CiAi's known scope includes a definition-oriented pipeline builder, read-only GitHub Actions observation/evidence collection, and a NuGet artifact **path inventory** verifier that checks caller-supplied relative paths.

That path verifier does not by itself inspect archive bytes, prove symbols are present inside a package, validate cryptographic digests, establish provenance, or authorize publication. The current observation and evidence capabilities do not imply a general workflow executor, trusted authorization system, deployment engine, or publishing engine.

Treat this section as a dated snapshot and re-audit the implementation before each release decision. Do not silently upgrade the wording when code changes; update the evidence and tests first.

## 12. Decisions and open questions

### Current working decisions

- Start with the hardest consequential workflow families to expose missing semantics.
- Design toward a common lifecycle model rather than a collection of unrelated YAML wrappers.
- Keep intent, observation, evaluation, authorization, execution, verification, and audit distinct.
- Preserve provider neutrality in the domain model; isolate provider-specific mechanisms in adapters.
- Use real workflows as evidence, with immutable references and an explicit confidence level.
- Validate simplification by walking from complex cases down to simple cases.
- Do not claim market frequency, broad support, safety, or release readiness without supporting evidence.

### Questions to resolve through sampling

- Which domain primitives recur across release orchestration, package publishing, deployment, and routine CI?
- Which concerns belong in the core model, and which are adapter-specific?
- What is the minimum useful authorization contract for a system that may eventually publish or deploy?
- How should partial success and uncertain external outcomes be represented and recovered?
- Which evidence must be immutable, and what retention/integrity guarantees are practical?
- What should a provider-neutral recipe compile into, and how can users inspect that plan?
- Which patterns can be made dramatically simpler without hiding important behavior?
- Which initial capability has the strongest evidence of user value relative to implementation and support cost?

## 13. Maintenance policy

Update this record when:
- A new sample changes a priority or exposes a missing state/invariant.
- A candidate primitive is accepted, rejected, or split.
- A proposed API becomes implemented and tested.
- A safety boundary or authorization model changes.
- An external example disproves a generalization.
- A readiness claim changes.

Every material conclusion should point to its supporting workflow sample, code/tests, or explicitly labeled hypothesis. Preserve the reason for decisions, including rejected alternatives, so later contributors do not unknowingly reintroduce old mistakes.

---

**Related document:** [CI Workflow Domain Catalog](CI_WORKFLOW_DOMAIN_CATALOG.md)  
**User-facing workflow guide:** [CI User Workflows](CI_USER_WORKFLOWS.md)  
**Release status:** [Release Readiness](RELEASE_READINESS.md)

## Source-level workflow audit

The first four-workflow source audit is recorded in [CI_WORKFLOW_SAMPLE_AUDIT.md](CI_WORKFLOW_SAMPLE_AUDIT.md). It covers CiAi contract verification, release-candidate observation, pinned cross-repository contract testing, and the FSM_API build/package workflow. The sample is intentionally small and ecosystem-biased; it reveals design questions but is not statistical market evidence. Findings are based on YAML review only until referenced application code, scripts, settings, and runs are separately checked.
