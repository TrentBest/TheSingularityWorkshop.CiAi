# Dependency Readiness Policy

## Purpose

A dependency gate answers two separate questions:

1. Is the provider's declared capability ready for this consumer?
2. If it is ready, is this exact provider output safe and authorized to consume?

An unfinished provider is not a failed provider. A consumer must be able to co-develop against a provider without treating the provider's incomplete work as a consumer failure. Conversely, a consumer must not silently consume an unverified package or infer that another repository's work is ready.

This policy extends the existing dependency gate and composite pipeline evaluator. It does not create another evidence ledger, scheduler, package authority, or human-approval mechanism.

## Readiness outcomes

| Outcome | Meaning | Consumer behavior |
|---|---|---|
| `Waiting` | Provider is developing, required output/evidence has not arrived, or provider execution is non-terminal | Keep dependent work dormant; do not fail the consumer; do not allocate an LLM turn |
| `Satisfied` | Exact declared capability from the exact provider candidate passed the required verified evidence policy | Admit only the dependent path bound to that evidence |
| `Blocked` | Provider has a terminal failure under the declared gate, capability is incompatible/revoked, evidence is invalid/stale, or policy denies consumption | Deny the dependent path; preserve the provider's failure attribution |
| `Optional` | The dependency is explicitly declared optional by the pipeline definition | Absence does not block; it never converts missing evidence into a satisfied required dependency |
| `Alternative` | The expression explicitly declares another complete path | A complete satisfied alternative may proceed; a partial branch never does |

Unknown states and policy versions fail closed. They must never be interpreted as `Satisfied`.

## Ownership and consumption

- The provider owns its package, capability implementation, source history, and provider-side evidence.
- The consumer owns its integration, compatibility checks, and consumer-side build/test results.
- A consumer may consume only a declared capability and version/source identity that the trusted evidence path has verified.
- Provider incompletion is a neutral hold on dependent work, not a failure of the consumer's own build.
- Consumer-owned failures remain consumer failures. Provider-owned failures remain provider failures.
- A terminal provider failure blocks only dependent paths that require the failed capability. A separately declared OR alternative may still satisfy the pipeline.
- Gate satisfaction does not grant repository-write, merge, package-publication, release, or deployment authority.

## Evidence identity

A satisfied provider gate must bind, at minimum:

- canonical provider repository;
- declared capability and compatible version/range;
- candidate identity;
- exact full source commit SHA;
- provider workflow identity and definition revision;
- run ID and attempt;
- required job/result/artifact identities and digests;
- gate policy ID and version.

A consumer must not substitute a different candidate, SHA, package, capability, run attempt, or policy version. Missing evidence is `Waiting` while the provider is legitimately in progress; malformed, contradictory, stale, or identity-mismatched evidence is `Blocked`.

## Development and release are distinct

A provider's `development` branch may produce a candidate suitable for co-development once its development gate is satisfied. That does not mean the candidate is a released package.

`master` and NuGet alignment remain the release boundary. A consumer that requires a published stable package must declare that stronger requirement. The pipeline must not silently substitute a development candidate for a stable release, or require a stable release when the declared contract permits verified development co-consumption.

## Dormancy and wake-up

The host/coordinator persists a `Waiting` work item as dormant. It does not repeatedly ask an LLM whether the provider has finished, poll through model turns, or consume token budget while nothing relevant has changed.

Only a relevant, authenticated provider gate event triggers reevaluation. Reevaluation uses the existing evidence authority and the exact gate leaf identities. Duplicate event delivery is idempotent; unrelated provider events do not wake the work item. A transition to `Satisfied` makes the dependent work eligible for normal allocation. A transition to `Blocked` remains denied until a new valid candidate/policy event changes the result.

CiAi evaluates the policy and evidence contracts. The host authenticates provider events and persists lifecycle state. The existing allocator controls budgets and dispatch. HeadlessAi receives work only after host admission.

## First consumer: raWWar / Physics

The first concrete dependency is raWWar consuming a declared capability from `TrentBest/TheSingularityWorkshop.Physics`.

- Physics still developing: raWWar's Physics-dependent work is `Waiting`; raWWar is not failed.
- Physics candidate completed but evidence is absent or untrusted: still `Waiting`; no consumption.
- Exact Physics candidate and capability verified: `Satisfied`; only that candidate is eligible for consumption.
- Physics provider gate terminally failed or capability is incompatible/revoked: `Blocked` for the dependent path.
- raWWar's own independent tests fail: report a raWWar failure, regardless of Physics readiness.
- A separately declared alternative can proceed only if its entire expression branch is satisfied.

Tracking: [CiAi issue #83](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/83) and [raWWar issue #11](https://github.com/TrentBest/raWWar/issues/11).

## Implementation status

CiAi now has executable composite AND/OR admission, deterministic Waiting/Satisfied/Blocked semantics, a versioned development-capability evidence parser, an independent TRX/provenance verifier, and typed development-capability leaves in that same evaluator. The raWWar consumer TRX/provenance artifact lane is green. The Physics provider artifact producer is under PR #64 and still requires fresh exact-head validation and independent artifact inspection. Durable host dormancy, authenticated/idempotent event ingress, relevant-event-only wake-up, and a live Physics → raWWar consumption proof remain outstanding. Do not claim end-to-end ADP readiness until those host and pilot gates pass.
