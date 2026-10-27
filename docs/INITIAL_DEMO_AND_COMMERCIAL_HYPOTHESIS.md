# CiAi First Demonstration and Commercial Hypothesis

**Trent Best · The Singularity Workshop**

**Status:** Working plan; the public demo and paid offer are not yet validated. This document distinguishes what can be demonstrated with the current read-only observer from future integration work.

## 1. The first customer hypothesis

**Initial buyer:** a maintainer or small engineering team responsible for several .NET libraries who already uses GitHub Actions and increasingly uses AI agents to help change code.

**Pain to test:** a green-looking CI summary is not enough to answer whether the exact proposed commit has all required checks, whether each check reached a terminal successful state, or whether the evidence actually belongs to that repository and revision. This becomes harder when an agent works across multiple repositories and package dependencies.

**Smallest credible paid offer:** a fixed-scope CiAi integration and evidence-policy review for one existing GitHub repository. Configure a required-workflow policy, produce reproducible JSON and Markdown evidence for an exact commit, test missing/mismatched/incomplete evidence cases, and deliver an operator runbook plus a prioritized gap report.

**Customer outcome:** reviewers can independently reproduce why a candidate satisfies—or fails—a declared evidence policy, without confusing observation with permission to publish.

**What is sold initially:** engineering time, integration, policy configuration, and verification. The MIT-licensed NuGet library remains separate. Do not sell a hosted governance service, automatic release control, or complete AI-driven CI orchestration until those capabilities and their security/operational models exist.

## 2. The reproducible demo

### Demo claim

For one public GitHub repository and one exact 40-character commit SHA, the current observer can collect matching workflow-run evidence and attempt-specific jobs/steps, then write JSON and Markdown reports. When an explicit required-workflow policy is configured, missing, ambiguous, incomplete, non-terminal, or unsuccessful required evidence fails closed. The output is read-only observation and policy evidence—not a release verdict or publication approval.

### Prepare

1. Install the .NET 8 SDK and clone this repository at the candidate revision being demonstrated.
2. Choose a public repository and copy its full commit SHA. Do not use a branch name, abbreviated SHA, or a run from a different revision.
3. Inspect the target repository's actual GitHub Actions workflow paths. Configure only the workflows the target's policy genuinely requires; do not invent names or treat unrelated workflows as mandatory.
4. Record the target repository, full SHA, the selected workflow paths, the CiAi source revision, and the policy identity in the demo notes.

### Run the observation

From the CiAi checkout, the README documents this command shape:

```bash
dotnet run --project samples/TheSingularityWorkshop.CiAi.ObserveRun -- OWNER/REPOSITORY FULL_40_CHARACTER_COMMIT_SHA artifacts/commit-observation.json
```

Replace the placeholders with real values. For public repositories, the observer can use unauthenticated read-only access where GitHub permits it. Set `CIAI_GITHUB_TOKEN` only when the target requires authentication; never put token values in commands, evidence files, screenshots, or reports.

Set `CIAI_REQUIRED_WORKFLOW_PATHS` to a semicolon-separated list of exact required workflow paths before running the command, for example:

```bash
export CIAI_REQUIRED_WORKFLOW_PATHS=".github/workflows/ci.yml;.github/workflows/tests.yml"
```

The paths above are examples only; replace them with paths verified in the target repository. The report must be inspected for its identity, completeness, terminality, and success fields. Without a configured required-workflow policy, the observer does not claim policy completeness.

### Demonstrate both outcomes

- **Evidence present:** choose a commit whose actual required workflows have completed successfully. Preserve the generated JSON and Markdown, then independently compare the reported repository, full SHA, workflow paths, run IDs, attempts, conclusions, and evidence URLs with GitHub Actions.
- **Fail closed:** use a separate known test case where a required workflow is absent, non-terminal, unsuccessful, or mismatched. If using a real repository state, label exactly which condition occurred; do not manufacture a provider result. For repeatable testing, prefer the existing offline contract tests that construct controlled evidence.
- **Negative claims:** show that an observer report does not publish packages, deploy applications, cancel provider runs, or grant owner authorization.

Retain the raw reports with the exact target repository and SHA, CiAi source SHA, observation time, required workflow policy, and relevant run/attempt identities. Redact sensitive values and avoid retaining secrets. The checked-in example is a command template, not evidence that a fresh demo has already been executed.

## 3. Claim ledger

| Classification | Accurate claim |
|---|---|
| **Implemented** | Read-only GitHub Actions observation; exact repository/full-SHA correlation; attempt-aware run/job/step collection with bounded pagination; JSON/Markdown observation reports; fail-closed required-evidence evaluation against supplied facts; declarative plan validation and deterministic recipe rendering. |
| **Demonstrable with a fresh run** | A public-repository observation for a chosen SHA, with reports independently checked against provider facts; a controlled offline failure case from contract tests. Attach fresh evidence before presenting this as a completed live demonstration. |
| **Partial / bounded** | An in-process sequential delegate executor and evidence evaluators exist, but they are not an authoritative GitHub Actions execution host. Existing cross-Cog checks prove an in-process contract path, not a remote end-to-end delivery pipeline. |
| **Planned / not to market as shipped** | Complete provider-hosted policy enforcement; a full end-to-end AI-driven workflow; automatic cross-repository dispatch; publication/deployment adapters; hosted governance service; automated owner authorization. |

A green workflow, successful evidence evaluation, recipe metadata, or model recommendation is not authorization to publish.

## 4. Commercial validation experiment

Run five short discovery conversations with maintainers who own multiple .NET repositories or use AI coding agents in an existing CI process. Do not lead with a proposed price; test the problem and the concrete deliverable first.

Ask:
1. When an AI agent changes code, how do you verify that required checks refer to the exact candidate commit?
2. What evidence do you need before accepting a change or preparing a release?
3. How are missing, skipped, duplicate, stale, or ambiguous checks handled today?
4. Who spends time assembling this evidence, and how often?
5. Would a fixed-scope setup that produces reproducible, fail-closed evidence and a documented policy solve a current problem? What would have to be included for you to pay for it?

**Validation threshold:** proceed only if at least two interviewees describe a recurring, consequential evidence/release-review problem and at least one agrees to a concrete next step such as a scoped repository assessment or paid pilot. This is a proposed experiment, not market research already completed.

## 5. Delivery scope, costs, and risks

**Pilot deliverables**
- One repository's explicit required-workflow policy and source-of-truth notes.
- A reproducible runbook and JSON/Markdown evidence examples tied to exact commit and workflow attempts.
- Positive and fail-closed test cases, including documented limitations.
- A short findings report with prioritized integration gaps.

**Cost/risk assumptions**
- Initial work is service-led; no hosted service is required.
- Customer workflow conventions and required checks vary, so scope must be capped at one repository and one policy profile.
- Public read-only observation may not cover private repositories or organization-specific access without customer-approved credentials.
- GitHub API limits, workflow reruns, incomplete evidence, and evolving provider semantics require explicit handling.
- No credential should be requested through chat or committed to source control. Any customer token should use the customer's approved secret-management path and least privilege.
- Do not promise automatic enforcement or publication prevention until a real host integration and security review demonstrate those guarantees.

## 6. Next release-readiness steps

1. Run the demo from a clean checkout and attach fresh, sanitized success and fail-closed evidence with exact provenance.
2. Review README and public API claims against the exact release candidate.
3. Re-run contract verification, package packing, package metadata validation, and artifact inspection on the same candidate SHA.
4. Resolve the open release gates in [RELEASE_READINESS.md](RELEASE_READINESS.md), including package/version confirmation and owner review.
5. Keep NuGet publication disabled until Trent explicitly authorizes the exact candidate.

A completed demo is evidence for a release review, not publication permission.

---

[Avatar.jpg](../Avatar.jpg)
