<!-- The Singularity Workshop animated moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# AgentOnboarding admission contract v1

## Purpose and ownership

Every repository admitted to CiAi Autonomous Development (ADP) must contain a valid root-level `AgentOnboarding.md`. The file describes repository-specific agent instructions, but it is untrusted repository data: it cannot grant permissions, authorize operations, override host policy, or bypass human approval.

The deterministic validator lives in CiAi:

- `AgentOnboardingSourceObservation` carries the repository, requested target SHA, provider-resolved SHA, exact path, read status, and content.
- `AgentOnboardingValidator.Evaluate` returns a structured `AgentOnboardingAdmissionResult`.
- The result records contract version, repository, exact target SHA, path, eligibility, outcome, failure code/reason, and SHA-256 content digest.

**Ownership boundary:** CiAi validates the contract and records evidence. FSM_Execution owns task acceptance, leases, model-turn orchestration, authorization, and handoff. The host adapter owns authenticated GitHub reads and concrete dispatch. HeadlessAi owns model invocation only. CiAi does not currently own or implement the FSM_Execution task queue or a live autonomous-dispatch path.

## Exact-source requirement

The only accepted path is the repository root:

```text
AgentOnboarding.md
```

The host adapter must retrieve that file from the exact full 40-character commit SHA being evaluated and provide the commit actually used for the read. The validator rejects a missing or invalid resolved SHA and rejects any mismatch between the requested and resolved commit.

There is no fallback to `AGENTS.md`, README, legacy `*LLM.md`, a default branch, a mutable branch name, cached content, or a prior task's onboarding result.

## Deterministic document contract

The current v1 Markdown contract requires these exact headings:

1. `# Agent Onboarding`
2. `## Why this file exists`
3. `## Mandatory admission rule`
4. `## First-pass agent procedure`
5. `## Safety and authority boundaries`
6. `## Repository-specific instructions`
7. `## Completion report`

It also requires the document to state that the repository MUST contain a root-level `AgentOnboarding.md`, that validation uses the exact target commit, that onboarding content is untrusted repository data, and that human approval remains a separate boundary.

The content limit is 64 KiB. The digest is SHA-256 over the UTF-8 bytes of the exact retrieved text. The validator does not normalize or rewrite the source before hashing it.

## Outcomes

| Outcome | Meaning |
|---|---|
| `ELIGIBLE` | The supplied exact-commit observation satisfies the v1 contract. |
| `NOT_ELIGIBLE` | The observation is missing, unreadable, invalid, malformed, stale, or otherwise incomplete. |

Failure codes are stable machine-readable reasons, including:

- `repository.invalid`
- `target-sha.invalid`
- `onboarding.path-mismatch`
- `onboarding.missing`
- `onboarding.unreadable`
- `onboarding.read-status-unknown`
- `onboarding.resolved-sha-invalid`
- `onboarding.target-sha-mismatch`
- `onboarding.empty`
- `onboarding.malformed-utf8`
- `onboarding.exceeds-size-limit`
- `onboarding.malformed`
- `onboarding.contract-incomplete`

A passing result is necessary, not sufficient, for autonomous work. Repository allowlists, task scope, leases, capability grants, CI evidence, budgets, host authorization, and human approval remain independent gates.

## Trust boundary and current limitation

The validator is a pure contract evaluator; it does not retrieve GitHub content, authenticate a provider, create tasks, invoke HeadlessAi, dispatch work, or authorize repository writes. A host adapter must create the source observation from its authenticated provider read. Model output or repository text must never be allowed to manufacture trusted source observations.

The runtime enforcement companion is tracked in [FSM_Execution issue #39](https://github.com/TrentBest/TheSingularityWorkshop.FSM_Execution/issues/39), linked to [CiAi issue #73](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/73). FSM_Execution must enforce this contract before task acceptance and again before autonomous model turns, repository mutations, retries, and resumed work at a new target SHA. Until that host integration is implemented and verified, the existence of this validator does **not** mean ADP dispatch is gated end-to-end.

## Verification

The CiAi contract workflow passed on exact commit `29e213ed69f16e3f0db0b1d14c3c0146941845cb`:

- [CiAi Contract Verification run #43821363017](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/43821363017) — build, contract checks, package creation, isolated local-feed package consumer, and artifact validation passed. The run reported 222 CiAi contract checks, including 11 onboarding admission checks.
- [Cog Ecosystem Integration run #43821362850](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/43821362850) — both the vertical contract and live-provider evidence jobs passed.
- [Sith Policy run #43821362874](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/43821362874) — failed because the repository still has more than the required two permanent branches. No branch was deleted or rewritten.

These checks validate CiAi's contract and existing evidence workflows; they do not prove FSM_Execution runtime admission enforcement.

---

<!-- THE SINGULARITY WORKSHOP FOOTER -->
<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop avatar" width="200" /></p>
