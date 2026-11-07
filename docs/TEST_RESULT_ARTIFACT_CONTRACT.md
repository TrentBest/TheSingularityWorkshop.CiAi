<!-- The Singularity Workshop moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# Provenance-bound test-result artifacts

## 01 — What

CiAi's test-result artifact contract turns a test-run artifact into a bounded, versioned diagnostic report for one exact GitHub Actions workflow attempt.

The first supported input is Visual Studio Test Results (`.trx`). The normalized output uses schema `ciai.test-results.v1` and preserves:

- Repository, candidate checkout ref/SHA, workflow path/definition ref/SHA, and event ref/SHA. Pull-request number/head/base identities and exact run/job evidence URLs are retained separately.
- Workflow run ID and attempt, plus the exact producing job ID and name.
- Provider artifact ID/name, provider-reported ZIP digest, extracted TRX digest, byte sizes, and retention timestamps.
- Test framework, total/passed/failed/skipped/other counts, stable test identities, bounded raw outcome values, normalized outcomes, bounded assertion messages, and bounded stack traces.
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
4. Pass the downloaded ZIP bytes and provider digest to `CiTestResultArtifactArchive.Read`. It verifies the complete archive digest before reading entries, then rejects path traversal, unexpected/duplicate entries, unsafe file types, oversized archives or payloads, and excessive compression ratios. It extracts only the expected TRX and provenance sidecar in memory.
5. Construct `CiTestResultArtifactSource` from provider metadata and the archive's verified digests/sizes. Bind the artifact ID/name to the exact repository, run, attempt, numeric job ID, job name, and job key.
6. Call the archive overload of `CiTestResultReport.ParseAndValidate`. It re-hashes both extracted payloads (so mutation of exposed memory is detected), parses the embedded provenance against its exact content digest, and compares every workflow/event/checkout/PR identity to the trusted v3 packet before parsing TRX with DTD processing disabled and strict size/record limits.
7. Configure `CiRequiredJobPolicy` with exact workflow path, YAML job key, and observed job name triples that CiAi considers required. The report derives `CiRequiredCheckStatus` from that policy and the validated v3 job observation; do not accept a caller's raw boolean or an externally supplied verdict.
8. Preserve the normalized report and its source digests for the next bounded HeadlessAi turn.

The host still owns provider authentication, exact artifact selection, download, and provider metadata verification. The archive reader owns digest-verified bounded ZIP inspection; the report overload owns embedded provenance binding and the CiAi verdict. The lower-level raw-TRX overload remains available for hosts that have already completed the equivalent trusted extraction and provenance checks.

### Trust and ownership

- **CiAi:** exact-run evidence, required-check policy, test-result normalization, verdict, reason codes, and per-attempt report preservation.
- **Trusted host:** provider authentication, artifact ZIP digest verification, safe extraction, workflow-definition trust policy, and mapping the artifact to the exact GitHub job.
- **HeadlessAi:** delivers the bounded report as untrusted context to the assigned LLM; it does not decide CI success.
- **Foundry:** correlates the report to the task, applies lifecycle and retry/time/cost budgets, and mediates authorized handoffs. It does not reinterpret test output or become a second CI verdict engine.
- **Repository host/owner:** retains approval of repository mutations and all privileged release actions.

## Canonical v1 report bytes and identity

The report model is the sole public CODE RED diagnostic payload: `ciai.test-results.v1`. The workflow/provenance packet is trusted input used by CiAi to derive this report; it is not a second downstream report contract.

Consumers must use `CiTestResultReport.ToCanonicalUtf8Bytes()` and must preserve those returned bytes unchanged. The canonical representation is defined as:

- UTF-8 encoding without a byte-order mark.
- Compact JSON with no indentation and no trailing newline.
- camelCase property names, in the `CiTestResultReport` declaration order.
- Enum values serialized as their exact string names (for example `ActionableFailure`).
- Null-valued properties are retained; no optional-property omission or consumer-side reserialization is permitted.
- The default `System.Text.Json` escaping rules are used. URI values are serialized as absolute URI strings; date/time values use the platform's ISO 8601 JSON representation.
- The maximum canonical report size is **1 MiB**. If the complete report exceeds this bound, serialization fails; diagnostics are never silently truncated to fit.

The report identity is exactly `sha256:` followed by the lowercase hexadecimal SHA-256 digest of the complete canonical UTF-8 byte sequence. `ReportSha256` exposes the lowercase digest without the prefix; `ReportIdentity` exposes the prefixed identity. These are derived metadata and are not serialized into the report body, avoiding a self-referential digest. The schema value and contract version remain `ciai.test-results.v1` and `1`.

`ToJson()` remains a human-readable diagnostic convenience and is **not** a wire-format or digest input. Do not hash its indented output. The accepted report bytes, identity, and digest are the exact values passed to Foundry and then opaquely to HeadlessAi.

The v1 verdict vocabulary is `CompletePass`, `ActionableFailure`, `IncompleteOrUntrusted`, and `NonActionableFailure`. The current stable reason codes are:

- `test-results.complete-pass`
- `test-results.actionable-failure`
- `test-results.artifact-missing`
- `test-results.required-check-incomplete`
- `test-results.required-check-status-mismatch`
- `test-results.check-not-required`
- `test-results.empty`
- `test-results.unknown-outcome`
- `test-results.failed-tests-in-green-job`
- `test-results.check-failed-without-test-failure`
- `test-results.failure-diagnostics-incomplete`

Reason codes are bounded producer output. Consumers preserve unknown future values as data but must not reinterpret them as a pass or actionable verdict.

## 04 — See it in a minute

### Contract usage

The preferred archive path keeps provider ZIP verification, embedded provenance, and TRX verdict production connected:

```csharp
var verifiedArchive = CiTestResultArtifactArchive.Read(
    downloadedZipBytes,
    providerArchiveDigest,
    "results.trx",
    "provenance.json");

var requiredJobs = new CiRequiredJobPolicy(new[]
{
    new CiRequiredJobDefinition(".github/workflows/ci.yml", "test-minesweeper", "test-minesweeper")
});

var report = CiTestResultReport.ParseAndValidate(
    verifiedArchive,
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

Both assertions reported expected `Accepted` and actual `RejectedTerminal`. The historical run did not upload a machine-readable TRX artifact. It is a fixture for the contract, not proof that CiAi already ingested or repaired that run. The current trusted producer workflow in Experiences PR #41 now names its candidate artifact `ciai-test-results-{runId}-{runAttempt}-test-minesweeper`, matching this contract's run/attempt/job-key convention. That alignment is present in the proposed workflow source, but remains unproven at runtime until the trusted candidate workflow is activated and an artifact is retrieved and validated.

### Verification status

The contract checks are registered in `TheSingularityWorkshop.CiAi.ContractTests`. In addition to TRX normalization and verdict coverage, the archive integration tests exercise the complete ZIP-to-verdict path, exact embedded run/attempt and checkout binding, provider-manifest substitution, post-extraction payload mutation, and the distinct archive-versus-payload size ceilings.

**Execution status:** The earlier GitHub Actions run [#41274930395](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/41274930395) passed on its then-current PR #36 head. The new archive-to-verdict integration commits are on stacked PR #45, whose base is another feature branch; the repository's PR workflow only targets `development` and `master`, and no exact-head run is currently available for this new head. Do not treat the earlier run as validation of these new changes. No local build/test result is claimed.

## 05 — Choose your next step

- Implementation and acceptance tracking: [CiAi issue #34](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/34).
- Provenance prerequisite: [CiAi issue #29](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/29) and [PR #30](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/30).
- Overall CI-to-agent loop: [CiAi issue #27](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/27).
- The trusted provider artifact retriever, production host integration, and HeadlessAi consumer handoff remain separate work. This contract does not claim those integrations are live.

<!-- THE SINGULARITY WORKSHOP FOOTER -->
<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop avatar" width="200" /></p>
