<!-- The Singularity Workshop moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# Provenance-bound test-result artifacts

## 01 — What

CiAi's test-result artifact contract turns a test-run artifact into a bounded, versioned diagnostic report for one exact GitHub Actions workflow attempt.

The first supported input is Visual Studio Test Results (`.trx`). The normalized output uses schema `ciai.test-results.v1` and preserves:

- Repository, candidate checkout ref/SHA, workflow path/definition ref/SHA, and event ref/SHA. Pull-request number/head/base identities and exact run/job evidence URLs are retained separately.
- Workflow run ID and attempt, plus the exact producing job ID and name.
- Provider artifact ID/name, provider-reported ZIP digest, extracted TRX digest, byte sizes, and retention timestamps.
- Test framework, total/passed/failed/skipped counts, stable test identities, outcomes, bounded assertion messages, and bounded stack traces.
- A CiAi-owned verdict and stable reason codes.

The verdict is one of:

| Verdict | Meaning |
|---|---|
| `CompletePass` | The required check passed and the complete TRX contains no failed tests. |
| `ActionableFailure` | The required check failed and every failed test has a bounded diagnostic. |
| `IncompleteOrUntrusted` | Provenance, required-check status, artifact availability, or result consistency is incomplete or untrusted. |
| `NonActionableFailure` | The required check failed, but test diagnostics do not identify a repairable test failure. |

A report is evidence, not permission. It gives HeadlessAi structured context; it does not authorize repository writes, reruns, merges, releases, or publication.

## 02 — Why

A normal CI job often stops at the first nonzero `dotnet test` exit code. That can prevent the workflow from uploading the very TRX file needed to explain the failure. The alternative—turning `continue-on-error` into a green job—destroys the meaning of the required check.

The contract therefore separates three facts:

1. **Test results:** what individual tests reported.
2. **Required-check outcome:** whether the configured required job passed or failed.
3. **Provenance and integrity:** which repository, checkout, workflow definition, run attempt, job, and artifact produced those results.

Those facts must agree. A TRX file from another run, another attempt, another candidate SHA, or another job is not usable evidence. A failed workflow with no test failures is not automatically repairable. A missing TRX is incomplete evidence, never a pass.

All test messages and stack traces are untrusted diagnostic data. Known token-like values are redacted before report serialization; message and stack-trace lengths are bounded. This is targeted redaction, not a guarantee that every possible secret format can be recognized.

## 03 — How

### Workflow-side artifact production

The opt-in .NET workflow must preserve the test exit code, upload TRX even after test failure, and then restore the failing exit code. The following is an illustrative job-step pattern; adapt the solution path and shell to the consumer repository.

```yaml
- name: Run tests and retain TRX
  id: tests
  shell: bash
  run: |
    set +e
    dotnet test ./YourSolution.sln \
      --logger "trx;LogFileName=results.trx" \
      --results-directory ./TestResults
    result=$?
    echo "exit_code=$result" >> "$GITHUB_OUTPUT"
    exit 0

- name: Upload test results
  if: always()
  uses: actions/upload-artifact@v4
  with:
    name: ciai-test-results-${{ github.run_id }}-${{ github.run_attempt }}-${{ github.job }}
    path: ./TestResults/**/*.trx
    if-no-files-found: error
    retention-days: 7

- name: Preserve test failure as job failure
  if: always()
  shell: bash
  env:
    TEST_EXIT_CODE: ${{ steps.tests.outputs.exit_code }}
  run: exit "${TEST_EXIT_CODE:-1}"
```

The artifact name binds the run, attempt, and workflow job key. The trusted host must map that job key to the numeric GitHub job ID and exact job name from the run's job inventory. If the TRX is absent or upload fails, the required job remains red and CiAi records incomplete evidence.

### Trusted ingestion sequence

1. Discover the exact workflow run and attempt, then collect the complete job inventory.
2. Select the artifact by exact run/attempt/job naming convention. Preserve its GitHub artifact ID, name, provider digest, size, creation time, and expiry.
3. Download the artifact through the trusted host. Verify the provider-reported SHA-256 over the complete ZIP bytes before extraction.
4. Safely extract only the expected TRX payload. Reject path traversal, unexpected entries, oversized archives, duplicate payloads, and malformed manifests.
5. Compute the extracted TRX SHA-256 and byte length. Bind the artifact ID/name/digests to the exact repository, run, attempt, numeric job ID, job name, and job key.
6. Parse the extracted bytes with DTD processing disabled and strict size/record limits.
7. Configure `CiRequiredJobPolicy` with exact workflow path, YAML job key, and observed job name triples that CiAi considers required. The report derives `CiRequiredCheckStatus` from that policy and the validated v3 job observation; do not accept a caller's raw boolean or an externally supplied verdict.
8. Preserve the normalized report and its source digests for the next bounded HeadlessAi turn.

`CiTestResultReport.ParseAndValidate` verifies the extracted payload digest and size, the v3 workflow/event/checkout provenance, and the exact job binding. It does not download the ZIP, verify the provider ZIP digest, or perform ZIP extraction. Those are trusted-host responsibilities and must be completed before constructing `CiTestResultArtifactSource`.

### Trust and ownership

- **CiAi:** exact-run evidence, required-check policy, test-result normalization, verdict, reason codes, and per-attempt report preservation.
- **Trusted host:** provider authentication, artifact ZIP digest verification, safe extraction, workflow-definition trust policy, and mapping the artifact to the exact GitHub job.
- **HeadlessAi:** delivers the bounded report as untrusted context to the assigned LLM; it does not decide CI success.
- **Foundry:** correlates the report to the task, applies lifecycle and retry/time/cost budgets, and mediates authorized handoffs. It does not reinterpret test output or become a second CI verdict engine.
- **Repository host/owner:** retains approval of repository mutations and all privileged release actions.

## 04 — See it in a minute

### Contract usage

The following is a source-checked API sketch. `v3Packet`, `verifiedTrxBytes`, and `source` must come from the trusted collection/retrieval path described above.

```csharp
var requiredJobs = new CiRequiredJobPolicy(new[]
{
    new CiRequiredJobDefinition(".github/workflows/ci.yml", "test-minesweeper", "test-minesweeper")
});

var report = CiTestResultReport.ParseAndValidate(
    verifiedTrxBytes,
    v3Packet,
    source,
    requiredJobs,
    observedAt);

if (report.Verdict == CiTestResultVerdict.ActionableFailure)
{
    // Deliver this immutable, provenance-bound report as untrusted LLM context.
    // A proposed repair still requires host-authorized repository capabilities.
}
```

### Concrete failure fixture

The Experiences Minesweeper acceptance scenario records a historical red run with 9 passing tests and 2 failures:

- `RevealingMineLosesAndTerminalActionsAreRejected`
- `ResetRestoresOriginalFixedMineLayoutAfterFirstRevealRelocation`

Both assertions reported expected `Accepted` and actual `RejectedTerminal`. The historical run did not upload a machine-readable TRX artifact. It is a fixture for the contract, not proof that CiAi already ingested or repaired that run.

### Verification status

The new contract checks are registered in `TheSingularityWorkshop.CiAi.ContractTests` (35 focused checks in the TRX report test group). They cover multi-failure normalization, exact provenance/job binding, digest and size mismatch, missing artifacts, pass/failure/non-actionable verdicts, redaction, bounded diagnostics, duplicate identities, inconsistent counters, and DTD rejection.

**Execution status:** source and test changes are committed on the feature branch. A .NET SDK was not available in this editing environment, so no local build/test result is claimed. Exact-head CI evidence is still required before this implementation can be considered validated.

## 05 — Choose your next step

- Implementation and acceptance tracking: [CiAi issue #34](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/34).
- Provenance prerequisite: [CiAi issue #29](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/29) and [PR #30](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/30).
- Overall CI-to-agent loop: [CiAi issue #27](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/27).
- The trusted provider artifact retriever, production host integration, and HeadlessAi consumer handoff remain separate work. This contract does not claim those integrations are live.

<!-- THE SINGULARITY WORKSHOP FOOTER -->
<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop avatar" width="200" /></p>
