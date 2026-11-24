# Destination-specific publication policy and Pages experience contract

**Status:** Initial policy contract implemented on `development`; GitHub Actions Pages host adapter, durable site assembly, and GUI.Blazer integration remain planned and unverified.

## Why publication policy belongs to the destination

A successful build is evidence that a candidate may be eligible for a destination. It is not a universal instruction to publish. Each destination declares its own mode and approval requirement.

| Destination policy | Approval | Result after complete, current, unambiguous successful evidence |
|---|---|---|
| Automatic Pages | None | Eligible for automatic publication |
| Automatic protected destination | Required | Awaiting approval |
| Manual destination | None | Awaiting manual action |
| Disabled destination | None | Disabled |

The current implementation is a pure policy evaluator. It does not invoke GitHub APIs, write workflows, access credentials, deploy, or grant host permissions. A host adapter must enforce the decision at the point of publication.

## Evidence required for eligibility

The policy evaluator requires all four facts to be true for the exact attempt:

- build succeeded;
- evidence is complete;
- the attempt is current, not superseded by a newer attempt;
- run identity is unambiguous.

A failed, cancelled/unsuccessful, incomplete, stale, or ambiguous attempt is blocked. The caller must bind the evidence to repository, source ref, full commit SHA, run ID, and attempt. Provider adapters remain responsible for proving those identities from authoritative provider records.

## Standard Blazer-backed Pages site

Every participating repository should converge on a stable presentation contract with two independently meaningful views:

1. **Operational dashboard:** latest attempt, current state, diagnostics, provenance, and evidence/recovery links.
2. **Last successful experience:** the most recent known-good site, documentation, demos, and assets, which remains accessible while later attempts fail.

A failed build updates operational visibility but does not promote its failed output or change the last-successful pointer. A successful build promotes its exact validated artifact and advances that pointer.

### Suggested site contract

```text
/
├── index.html                    # stable GUI.Blazer shell / dashboard entry
├── dashboard/
│   └── status.json               # latest attempt and diagnostics
├── experience/
│   ├── current.json              # pointer to last successful publication
│   └── releases/<full-source-sha>/ # immutable successful site content
└── assets/
```

This is a proposed logical layout, not yet a generated or deployed structure. The retention source must outlive short-lived Actions artifacts. The publication host must assemble and deploy one complete site artifact; GitHub Pages deployments replace the site as a whole, so independent dashboard and experience deployers must not race or overwrite one another. Serialize publication and update the current pointer only as part of a successful, verified promotion.

### Required status distinctions

The dashboard must not conflate the latest attempt with the last successful publication. At minimum expose:

- latest attempt identity and state;
- last successful publication identity and URL;
- whether the current attempt changed the last-successful pointer;
- diagnostic/evidence completeness;
- provider workflow/run/attempt and deployment links when available.

## CiAi's coordination boundary

CiAi coordinates contracts and evidence across specialized builders, validators, artifact producers, host adapters, deployment providers, and presentation consumers. It does not need to own each domain's build logic. GUI.Blazer consumes standardized metadata and renders a consistent front door; repository-specific experience content remains owned by its repository.

## Implementation status

| Capability | Status |
|---|---|
| Destination-specific automatic / manual / disabled policy | Implemented as a pure contract and evaluator |
| Approval-required destination mode | Implemented as a pure contract and evaluator |
| Fail-closed exact-attempt eligibility inputs | Implemented as a pure evaluator; provider binding remains future work |
| Deterministic Pages plan and workflow generation | Not implemented |
| GitHub Actions Pages host adapter and deployment evidence | Not implemented |
| Durable immutable last-good content retention | Not implemented |
| Single serialized full-site publisher and rollback proof | Not implemented |
| Standard Blazer shell and metadata producer/consumer | Not implemented |
| GUI shadow parity and consumer migration | Not started; existing consumer workflows remain untouched |

See [issue #91](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/91) for the complete capability acceptance criteria.

## Implemented Pages plan slice

The first deterministic Pages declaration now exists in `CiPagesPublicationPlan`. It accepts a repository, safe relative source path, source branch, artifact name, and plan ID, then expands into the existing `CiWorkflowPlanDraft` contract:

1. build the site;
2. validate the generated site;
3. package the exact validated site artifact;
4. deploy that artifact to Pages.

The expansion is deterministic, provider-neutral, and side-effect free. It rejects absolute/traversal/platform-dependent source paths, unsupported source branches, malformed identifiers, and invalid repository identities. The resulting destination policy is explicitly automatic with no human approval, while the existing evidence evaluator still blocks failed, incomplete, stale, or ambiguous attempts.

This slice does **not** generate GitHub Actions YAML, invoke Actions, upload or retain artifacts, deploy Pages, or implement the Blazer shell. Those remain separate host-adapter and publication-lifecycle work. Contract tests are registered in the repository's executable contract-test runner; workflow evidence is pending until CI runs against the registration commit.
