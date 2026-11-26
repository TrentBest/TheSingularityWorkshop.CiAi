# CiAi and ADP responsibility boundaries

**Status:** architecture contract; implementation and cross-repository proofs are tracked separately. This document assigns decision ownership. It does not claim every integration is complete.

## The governing rule

Every consequential decision has one authoritative owner. Other components may validate, transport, present, or enforce that decision at their own boundary, but must not silently create a competing authority.

Keep these predicates distinct:

- **Identity:** which stable entity is being referenced?
- **Authentication:** which external principal has been authenticated?
- **Verification:** what fact has been established, by whom, and with what provenance?
- **Authorization:** may this principal perform this operation in this context?
- **Entitlement:** what product capacity/capabilities has the trusted issuer granted?
- **Admission:** may this specific repository/ADP request proceed under policy and capacity?
- **Execution:** what accepted work is run, with what lifecycle and result?
- **Evidence:** what did the external CI provider actually run and report?
- **Approval:** which consequential action has a human explicitly authorized?

A positive answer to one is not proof of another.

## Ownership matrix

| Component | Owns | Explicitly does not own |
|---|---|---|
| **Profiles** | Provider-neutral entity/profile semantics; stable opaque ProfileId; identity-link and external-reference contracts; claims, provenance metadata, disclosure, exclusions, observer-specific representations | Authentication, credentials/tokens, account-link authorization, commercial entitlement, ADP/repository capacity, billing, execution |
| **Authenticated host** | Authentication/session security; account-link authorization; validation of authenticated provider principal; access control; trusted integration composition; human-approval enforcement | Inventing identity from request data; treating repository access as organization authority; changing CiAi entitlement; blindly executing model output |
| **CiAi** | CI observation/evidence schemas; exact-run/exact-SHA provenance validation; CI policy and verdicts; entitlement contract verification; ADP identity/capacity; repository enrollment/admission policy | Authenticating people; raw credential storage; workflow execution; provider run truth; full Profile access; human approval |
| **CI provider (GitHub Actions)** | Actual workflow run/attempt/job/step execution facts, checked-out commit facts, provider artifacts and status | Product licensing, ADP ownership, application authorization, human approval |
| **FSM_Execution** | Host-neutral accepted-work lifecycle; validation and execution coordination; leases, cancellation, durable execution records, structured outcomes | Authentication, Profile resolution, entitlement verification, repository/ADP admission policy, CI provider truth, UI/presentation |
| **HeadlessAi** | Bounded/resumable LLM turns, model interaction lifecycle, normalized turn results | Authorization, entitlement, repository policy, privileged side effects, trust in generated text |
| **CommandAi** | Generic versioned atomic command/proposal representation and codec | Authorization, licensing, policy verdicts, repository mutation execution |
| **GrammarAi** | Structural grammar and typed-shape validation | Identity proof, authorization, entitlement, CI policy verdict, execution |
| **ProtocolAi** | Stable protocol/category/member identity, correlation, provenance and idempotency semantics | Identity proof, authorization, entitlement, policy verdict, execution |
| **Each repository** | Its source, build/test/package definitions, repository-specific domain truth and CI workflow definitions | Licensee identity, commercial entitlement, cross-repository ADP capacity, override of host/CiAi admission |
| **Application UI** | Presentation, explicit user intent, showing limits/status/consent, forwarding requests | Source of truth for identity, entitlement, CI evidence or execution lifecycle |
| **Human owner/operator** | Approval of consequential operations; release/merge/deploy decisions; branch deletion; unresolved policy choices | Being bypassed by an LLM, inferred intent or a default setting |

### Important distinction: host versus Execution

The host is the security and composition boundary. It authenticates the caller, resolves trusted identity, verifies authorization, obtains CiAi admission, and issues narrowly scoped capabilities.

FSM_Execution is the execution engine. It accepts only host-authorized work, validates the request and capabilities, runs it, and reports lifecycle state. It must not authenticate a person or decide whether that person has paid for an ADP.

Execution may enforce a host-issued capability at runtime, but that enforcement does not make Execution the authority that grants the capability.

## Canonical identity-to-execution path

```text
External provider principal
          |
          v
Authenticated host
  authenticate + authorize account link
          |
          v
Profiles
  verified issuer/subject binding -> stable ProfileId
          |
          v
CiAi
  verify entitlement + ADP/repository admission
          |
          v
Host
  issue scoped execution request/capabilities
          |
          v
FSM_Execution
  validate -> execute -> retain lifecycle/outcome
```

In parallel, GitHub Actions remains authoritative for CI run facts. CiAi observes the provider's exact run/attempt/commit evidence and evaluates it against CiAi policy. HeadlessAi may propose bounded changes, while CommandAi/GrammarAi/ProtocolAi provide representation and structural contracts. The host independently authorizes every consequential operation.

## Data and trust boundaries

1. The host authenticates a provider principal. A Profile mapping object, email, GitHub username, repository permission, or request-supplied ProfileId is not authentication evidence.
2. The host authorizes account linking and resolves the verified provider issuer/subject through the Profiles binding store to one stable ProfileId.
3. CiAi verifies an issuer-backed entitlement for that ProfileId and requested operation. A tier label or client assertion is not entitlement proof.
4. CiAi returns only a purpose-bound admission projection: decision, reason, explicit limits/capabilities, entitlement/version references, and minimal status metadata. It never sends a full Profile to the pipeline.
5. The host creates a scoped Execution request only after admission. Execution rejects missing, expired, mismatched, revoked, or unverifiable capability material before side effects.
6. CI provider evidence is immutable input to observation. CiAi validates provenance; it does not fabricate or rewrite provider state.
7. Model output is untrusted proposal data. It cannot authorize itself, bypass admission, or invoke privileged operations directly.

## Product identity and capacity

- ProfileId identifies the licensee; it is not a credential or entitlement.
- An ADP has a stable identity owned by CiAi and one authenticated licensee owner.
- Repositories are members of an ADP; repository membership does not establish licensee ownership.
- Repository capacity and ADP capacity are separate counters and are checked independently and atomically.
- One ADP can span multiple repositories. A repository must not silently create another ADP.
- Multiple ADPs per repository remain denied until an explicit product policy says otherwise.
- Existing pipeline records/evidence remain governed by a documented access/export/suspension policy even when new capacity is denied.

## Cross-cutting invariants

- Well-formed data is not necessarily trusted data. Verify source, issuer, signature/provenance, freshness, subject binding, and allowed purpose where applicable.
- Unknown, stale, ambiguous, mismatched, revoked, or unverifiable authority fails closed for new side effects.
- Keep credentials and provider tokens in the host/provider boundary. Never copy them into Profiles, CiAi evidence, model prompts, or Execution journals.
- Record decisions with stable references and minimal metadata. Do not duplicate personal data or entire Profiles into logs.
- Every cross-boundary contract names its owner, schema/version, purpose, trust assumptions, and failure semantics.
- Avoid shared mega-databases and universal services that absorb unrelated responsibilities.
- Human approval, merge, release, deployment, publication, and branch deletion remain explicit human-controlled actions unless a separately reviewed policy grants a narrower automation.

## Integration acceptance tests

The complete vertical proof should test each seam independently:

1. Authenticated external subject resolves to exactly one stable ProfileId; unverified/ambiguous/revoked mappings fail.
2. ProfileId alone does not grant entitlement; missing, expired, revoked, mismatched, or untrusted entitlement fails.
3. Entitlement and repository/ADP capacity checks are independent and atomic under concurrency.
4. Admission output contains only allowlisted fields; unrelated Profile claims never cross into CiAi/Execution.
5. Denied admission causes no queue insertion, scheduling, observation, planning, or execution side effect.
6. Host-issued scoped capabilities are required by Execution; caller-supplied identity or tier labels are ignored.
7. Exact-SHA provider evidence is independently validated; green CI evidence is not release/merge permission.
8. Model proposal, grammar validity, protocol validity, authorization, and human approval are independently tested predicates.

## Related implementation records

- [CiAi #97 — responsibility-boundary reconciliation](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/97)
- [CiAi #96 — privacy-minimal client/licensee data](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/96)
- [CiAi #95 — ADP capacity and licensee identity](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/95)
- [CiAi #93 — repository allowance](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/93)
- [Profiles #8 — stable identity integration](https://github.com/TrentBest/TheSingularityWorkshop.Profiles/issues/8)
- [FSM_Execution #51 — authenticated admission](https://github.com/TrentBest/TheSingularityWorkshop.FSM_Execution/issues/51)
- [FSM_Execution #52 — end-to-end ADP proof](https://github.com/TrentBest/TheSingularityWorkshop.FSM_Execution/issues/52)

This map is a responsibility contract, not a claim that every seam is already implemented. Follow the linked issues and exact CI evidence for implementation status.
