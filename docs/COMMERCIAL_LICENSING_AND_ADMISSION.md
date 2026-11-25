# CiAi commercial licensing and repository admission

## Product boundary

CiAi is the Workshop's commercial product. Other Singularity Workshop
repositories remain available under their own respective licenses; this
commercial license applies only to CiAi material owned or controlled by
The Singularity Workshop.

CiAi is source-available for inspection. The current license grants free
internal use on up to five configured repositories to individuals,
non-profits, educational projects, and commercial organizations alike.
More than five configured repositories requires a paid entitlement.

This is a product policy and implementation contract, not a published price
schedule. Paid tier thresholds and prices must be set after analysis of
representative GitHub organization data and customer discovery.

## Repository counting rule

- Count distinct canonical `owner/repository` identities, case-insensitively.
- Count public and private repositories equally.
- Count a repository once per licensee, even if multiple CiAi workflows,
  agents, teams, or users refer to it.
- Aggregate across the licensee's GitHub organizations and user accounts.
- Parent, subsidiary, and commonly controlled entities share one allowance
  unless a written enterprise agreement defines a separate licensee.
- A repository remains counted while configured, even if inactive or archived.
- A fork is a separate repository if it is configured for CiAi.
- Reconfiguring the same repository must not consume another slot.
- The first five distinct repositories are eligible under the free grant.
  The sixth and subsequent repositories require a paid entitlement.

The count is not the number of repositories that happen to be running at one
instant. It is the number the licensee has configured CiAi to operate on.

## Admission gate

The gate belongs at the central repository-enrollment/admission boundary,
before a newly configured repository can enter discovery, scheduling, CI
observation, ADP planning, or work queues. A UI-only warning is insufficient.

Admission must be atomic with the entitlement check. Two concurrent requests
for the fifth and sixth repositories must not both pass based on the same
stale count. The host should reserve an entitlement slot or perform a
transactional check-and-enroll against the authoritative license store.

Required decision inputs:

- authenticated licensee/account identity;
- canonical candidate repository identity;
- current configured repository identities;
- entitlement tier, maximum repository count, and validity period;
- source and version of the entitlement record.

Required outcomes:

- **Admitted** — the candidate is already configured, or a new distinct
  repository fits within the current entitlement.
- **UpgradeRequired** — a new repository would exceed the authorized limit.
- **EntitlementUnavailable** — the entitlement cannot be verified; fail
  closed for new enrollment without disrupting already admitted repositories.
- **InvalidRequest** — identity or repository data is malformed.

The admission gate must not delete existing configuration or interrupt
previously admitted repositories when a new repository is rejected.

## Upgrade-required report

A rejection must be a normal, machine-readable CiAi report, not an exception
that disappears into logs. It should include:

- stable result code: `CIAI_LICENSE_REPOSITORY_LIMIT_EXCEEDED`;
- outcome: `UpgradeRequired`;
- licensee reference (opaque identifier; do not expose billing secrets);
- candidate repository identity;
- current configured repository count;
- maximum authorized repository count;
- number of additional repositories requested;
- entitlement tier and entitlement status;
- timestamp and correlation ID;
- a clear explanation that no work was queued or started for the candidate;
- a purchase/upgrade URL supplied by the host's commercial configuration.

The report should state, for example:

> CiAi did not enroll this repository. This account has 5 configured
> repositories and is authorized for 5. A paid license is required to add
> another repository. No CiAi work was started for this repository.
> Upgrade your CiAi license: [configured purchase URL]

The purchase URL must come from trusted host configuration or a verified
billing provider response. Do not hard-code a fake checkout URL. Until a
checkout endpoint exists, provide the official CiAi licensing/contact page
as an informational link and label it accurately.

## Security and reliability requirements

- The entitlement service is authoritative; local JSON and user-supplied
  environment variables cannot self-grant a larger limit.
- Signed or authenticated entitlement data must bind the licensee, tier,
  repository limit, validity interval, and issuer.
- Cache verified entitlements with explicit expiry and bounded offline
  behavior. Never silently turn an unavailable paid entitlement into an
  unlimited grant.
- Make check-and-enroll idempotent and concurrency-safe.
- Do not send payment-card data or billing secrets into CI logs, model context,
  repository manifests, or reports.
- The gate controls admission, not the trustworthiness of CI evidence. It must
  remain independent of test verdicts, human approval, and publication gates.
- Existing admitted repositories continue according to the applicable
  entitlement/grace policy; rejection of a new repository must not cancel
  unrelated work already in progress.

## Implementation sequence and current status

**Implemented in the current development PR:** typed entitlement, admission
request, decision, and report contracts; a deterministic evaluator for
canonical identity, case-insensitive de-duplication, existing-repository
idempotency, verified entitlement validity, and the five-repository free
limit; contract tests for the first five/sixth repository, duplicates,
case variants, forks, malformed identities, paid allowances, expired and
unavailable entitlements, and upgrade URL validation.

**Still required before the financial gate is operational:**

1. Integrate the evaluator at the host's actual repository enrollment point,
   before discovery, queue insertion, observer scheduling, or ADP planning.
2. Add an authenticated entitlement provider and transactional or
   reservation-based check-and-enroll operation.
3. Add a customer/billing portal and configure its trusted upgrade URL.
4. Add host-level concurrency tests and an end-to-end proof that the sixth
   repository is refused before any work starts, while the first five
   continue normally.

The pure evaluator is not the security boundary by itself. It cannot
authenticate a licensee, reserve a slot, prevent concurrent races, or stop a
host that ignores its decision. The host must supply authenticated identity
and enforce the decision atomically.

## Pricing research

Do not derive prices from a handful of famous organizations or from the
maximum repository count GitHub permits. Estimate the empirical distribution
of repositories per organization, then distinguish organizations that are
individual projects, small teams, growing product companies, and very large
enterprises.

A defensible research sample should:

1. use a large, reproducible organization dataset with organization size
   metadata and repository counts;
2. separate public repository counts from total private-plus-public counts
   wherever available;
3. report median, 75th, 90th, 95th, 99th percentiles and the long tail;
4. segment by organization size and company/industry metadata where reliable;
5. report coverage, missingness, stale/inactive accounts, and sampling bias;
6. avoid treating public repository count as total CiAi workload;
7. test candidate tier boundaries against percentile bands and customer
   interviews before publishing prices.

The initial five-repository free allowance is a product policy, not a claim
that five is a statistically optimal boundary. Pricing should be revisited
after real opt-in CiAi usage data exists.
