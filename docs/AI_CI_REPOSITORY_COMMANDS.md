<!-- The Singularity Workshop animated moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# AiCi repository command proposal contract

## Purpose

AiCi composes repository-development intent into enriched CommandAi/1 atomic commands. It does not access Git, create branches, edit files, run builds, or authorize changes.

## Field mapping

| Repository operation data | CommandAi field |
|---|---|
| owner/repository identity | Target (typed literal text) |
| isolated feature branch and exact base commit SHA | Scope (typed literal text) |
| action identity | ActionId |
| project/file path, content, expected source SHA-256, patch, package ID, package version | Ordered typed arguments |
| stable identity for the proposed command | CommandId (deterministically derived GUID) |
| stable retry identity | IdempotencyKey (separate deterministic opaque string) |
| repository-operation expected status/detail/result SHA shape | OutcomeContract |
| live base-SHA and file-digest checks | Host-owned validation; not yet encoded as CommandPrecondition |

The target and scope values are opaque to CommandAi. The consuming host owns their meaning and must revalidate them against live repository state.

## Deterministic identity

AiCi derives a SHA-256 digest from a length-prefixed binary representation of repository identity, branch, base SHA, action identity, command position, and ordered typed argument values. The first 16 digest bytes form the command GUID; the full lowercase digest forms the idempotency identity with a CiAi prefix.

Equivalent builder calls with identical context and operations therefore produce byte-for-byte identical proposals. The command position distinguishes repeated identical operations within one proposal. A changed repository, branch, base SHA, action, argument, or position produces a different identity.

This derivation supplies stable proposal identities; it does not implement replay protection or guarantee exactly-once execution. The host must persist and enforce idempotency semantics.

## Expected outcome

The shared AiCi outcome contract declares:
- required status field;
- optional detail field;
- optional resulting commit SHA field.

These fields describe an expected result shape. They are not a CI verdict, execution receipt, or evidence that an operation succeeded. Actual outcomes are correlated and interpreted by the host/runtime; CI evidence remains CiAi-owned.

## Preconditions

The builder currently emits an empty preconditions collection. The base SHA is carried in the command scope, and modify operations carry the expected file-content SHA-256 as a typed argument. The host must compare these values with live state immediately before mutation.

AiCi deliberately does not invent a grammar version or expression. A future structured precondition requires a registered GrammarAi grammar and a trusted host evaluator. Until then, the absence of a precondition does not waive host-side revalidation.

## Safety boundary

- Only isolated feature branches are accepted; master and development are rejected.
- Repository identity and base commit are explicit in target/scope.
- File paths are repository-relative and reject traversal, roots, and Git metadata.
- Modify operations require a full expected SHA-256 digest.
- Payloads and proposal size are bounded by CommandAi limits.
- CommandAi validates the bounded representation only.
- Foundry/host owns live-state validation, replay/delivery, authorization, approval, dispatch, and execution.
- Package publication, release, and deployment remain separate explicit gates.

## Verification

The AiCi contract tests cover deterministic command and idempotency identities, target/scope mapping, expected outcome declarations, operation-specific typed arguments, path and branch validation, payload bounds, and immutable builder snapshots. The PR's exact-head workflows additionally verify the local CommandAi source pin, package consumer, and Cog integration.
