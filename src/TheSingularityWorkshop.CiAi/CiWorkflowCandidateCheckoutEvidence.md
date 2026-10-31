<!-- The Singularity Workshop moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# CiWorkflowCandidateCheckoutEvidence

**User moniker:** Candidate Commit Proof

## Purpose

`CiWorkflowCandidateCheckoutEvidence` identifies the exact GitHub Actions job and steps that prove a trusted workflow resolved and checked out one isolated feature-branch candidate commit.

It is attached to `CiWorkflowEvidencePacket` v2 when the workflow execution ref differs from the code-under-test ref.

## Required identity

- `CandidateRef`: a valid isolated `refs/heads/feature/*` ref.
- `CandidateSha`: a full 40-character commit SHA, normalized to lowercase.
- `JobName`: the exact job name containing the provenance steps.
- `ResolutionStepName`: the exact step that resolves the candidate branch to its immutable SHA.
- `VerificationStepName`: the exact step that verifies the checked-out commit equals that SHA.

Resolution and verification step names must be distinct. All identity fields are bounded and control-free.

## Packet verification

`CiWorkflowEvidencePacket` does not accept a caller-supplied boolean as proof. It derives `CandidateCheckoutVerified` from the raw `CiWorkflowJobObservation` facts. Verification succeeds only when exactly one matching job exists, that job belongs to the packet's exact run/attempt, and exactly one matching resolution step and verification step both completed successfully. The job's later build/test conclusion is deliberately independent: a later test failure does not erase proof that the candidate was checked out.

If a required job or step is absent, skipped, failed, duplicated, or ambiguous, candidate checkout is unverified. A later job/run failure still makes `IsSuccessful` false even when checkout provenance remains verified. The packet preserves the evidence and exposes `candidate.checkout-unverified` in its blocking reasons.

Separate candidate evidence is only valid when the workflow-definition ref is the trusted `refs/heads/development` ref. The host must still validate the workflow SHA against its trusted workflow revision policy.

---

<!-- THE SINGULARITY WORKSHOP FOOTER -->
<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop" width="200" /></p>
