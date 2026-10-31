# CI workflow rerun request contract

The rerun request contract defines the bounded instruction a trusted coordinator may submit when it needs CiAi to revalidate one exact candidate commit. It is a policy boundary, not a network endpoint and not a GitHub Actions dispatcher.

## Request identity

A version 1 request carries:

- request, task, and correlation identifiers;
- canonical owner/repository identity;
- a full 40-character candidate commit SHA and exact `refs/heads/feature/...` ref;
- one explicit workflow path under `.github/workflows`;
- an idempotency key;
- attempt number and configured maximum (hard-capped at five);
- request and expiry timestamps (hard-capped at 30 minutes).

The contract rejects `master`, `development`, tags, malformed Git refs (including empty/dot path components, `.lock` suffixes, control/space characters, and Git-special syntax), nested workflow paths, non-workflow paths, incomplete SHAs, and unbounded retry windows. Workflow identities are restricted to root-level files directly under `.github/workflows`, matching GitHub Actions' workflow discovery layout.

## Trusted host authorization

`CiDispatchAuthorizationContext` contains normalized issuer, audience, subject, credential identifier, scopes, repository grants, and credential validity times. The required scope is `ciai.workflow.dispatch`.

**This object is not a credential verifier.** The authenticated host adapter must validate the credential signature, issuer, audience, expiry, and revocation state before constructing this context. Never populate it from unverified HTTP headers, callback JSON, issue text, or model output. Raw tokens and secrets are not represented in the contract and must never enter evidence packets or logs.

The policy also checks a fresh `CiWorkflowRerunTarget` obtained from GitHub immediately before dispatch. Repository, ref, full SHA, and workflow path must all match exactly; a moved branch is stale and must be rejected rather than silently dispatching against a different commit.

## Policy outcomes

`CiWorkflowRerunRequestPolicy.Evaluate` returns stable blocking reason codes rather than dispatching work. It enforces the configured repository/workflow allowlists, required scope, issuer/audience, repository-specific grant, credential validity, request expiry, and exact target match.

The caller must persist request/idempotency state and enforce the total task retry budget across requests. The per-request attempt cap is not a distributed rate limiter or a substitute for Foundry's task-level budget.

## Durable acceptance ledger

`FileCiWorkflowRerunRequestLedger` evaluates the policy before appending an accepted request to a local append-only journal. It flushes before reporting acceptance, deduplicates exact request retries, rejects conflicting reuse of a task-attempt identity, enforces sequential attempts and a stable task-level maximum, and rehydrates accepted requests after restart. An incomplete final journal record fails closed.

The ledger records acceptance, not execution. A caller still owns dispatch state, callback retry scheduling, delivery acknowledgement, and task lifecycle. The journal is local storage, not a distributed coordination service.

## Not implemented by this contract

This slice does not expose an HTTP callback, validate cryptographic credentials, retrieve GitHub branch state, dispatch `workflow_dispatch`, wait for a run, or deliver the resulting evidence packet. Those operations belong in a trusted host adapter and must retain GitHub provenance. No rerun request authorizes code changes, merge, publication, deployment, or release.
