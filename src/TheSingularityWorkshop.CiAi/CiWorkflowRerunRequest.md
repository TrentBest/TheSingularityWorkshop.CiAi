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

The policy also checks a fresh `CiWorkflowRerunTarget` obtained from GitHub immediately before dispatch. Repository, ref, full SHA, and workflow path must all match exactly; a moved branch is stale and must be rejected rather than silently dispatching against a different commit. For the workflow-dispatch transport, the host must select the trusted `refs/heads/development` workflow definition and pass the candidate feature branch as a separate input. Never dispatch the candidate branch as the workflow-definition ref: candidate code must not supply the workflow that evaluates itself. The run must retain both workflow-definition ref/SHA and candidate ref/SHA as separate provenance fields.

## Policy outcomes

`CiWorkflowRerunRequestPolicy.Evaluate` returns stable blocking reason codes rather than dispatching work. It enforces the configured repository/workflow allowlists, required scope, issuer/audience, repository-specific grant, credential validity, request expiry, and exact target match.

The caller must persist request/idempotency state and enforce the total task retry budget across requests. The per-request attempt cap is not a distributed rate limiter or a substitute for Foundry's task-level budget.

## Opt-in GitHub Actions workflow input contract

CiAi's own `.github/workflows/ci.yml` now exposes an optional correlated dispatch input group:
- `target_branch`: the isolated feature branch name (without `refs/heads/`);
- `ciai_request_id`, `ciai_correlation_id`, and `ciai_task_id`;
- `ciai_candidate_sha`: the exact full candidate SHA;
- `ciai_attempt_number`: 1 through 5.

The five `ciai_*` values are all-or-none. Every `workflow_dispatch` run must use trusted `refs/heads/development` as its workflow-definition ref; never dispatch the candidate feature branch as the workflow-definition ref. When the CiAi input group is supplied, the workflow validates the isolated `feature/*` target, resolves that branch again from the remote, requires the resolved head to equal `ciai_candidate_sha`, and fails before build/test if it moved. It then checks out the immutable candidate SHA and records both workflow-definition and candidate provenance. Push/PR runs are unaffected. Manual workflow dispatches must also select `development`; the optional `target_branch` input can still select a candidate to test from that trusted workflow ref.

`CiWorkflowDispatchInputContract.CreateInputs` creates the exact six-field mapping as an immutable dictionary and contains no credential fields; see its [class companion](CiWorkflowDispatchInputContract.md).



Because the trusted workflow run's GitHub `headRef`/`headSha` identify `development`, evidence packet v2 serializes these as `workflowRef`/`workflowSha` and carries `candidateRef`/`candidateSha` separately. `CiWorkflowCandidateCheckoutEvidence` identifies the exact job, branch-resolution step, and checkout-verification step; the packet derives `CandidateCheckoutVerified` from those raw job/step outcomes. Foundry must consume v2 and reconcile both SHA roles. `FromJson` rejects v1 rather than silently treating the workflow SHA as the candidate SHA.

This is the opt-in workflow-side contract only. The authenticated HTTP dispatcher that supplies these inputs, its durable dispatch-intent ledger, and run receipt/reconciliation client are not yet implemented. A caller must not treat a green run summary as accepted evidence until it reconciles the exact repository, workflow, run, attempt, and candidate SHA.

## Durable acceptance ledger

`FileCiWorkflowRerunRequestLedger` evaluates the policy before appending an accepted request to a local append-only journal. It flushes before reporting acceptance, deduplicates exact request retries, rejects conflicting reuse of a task-attempt identity, enforces sequential attempts and a stable task-level maximum, and rehydrates accepted requests after restart. An incomplete final journal record fails closed.

The ledger records acceptance, not execution. A caller still owns dispatch state, callback retry scheduling, delivery acknowledgement, and task lifecycle. The journal is local storage, not a distributed coordination service.

## Not implemented by this contract

This slice does not expose an HTTP callback, validate cryptographic credentials, retrieve GitHub branch state, dispatch `workflow_dispatch`, wait for a run, or deliver the resulting evidence packet. Those operations belong in a trusted host adapter and must retain GitHub provenance. No rerun request authorizes code changes, merge, publication, deployment, or release.
