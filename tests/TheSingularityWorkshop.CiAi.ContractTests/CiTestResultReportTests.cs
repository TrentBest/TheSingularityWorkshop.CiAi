using System.Security.Cryptography;
using System.Text;
using System.Xml;
using TheSingularityWorkshop.CiAi;

internal static class CiTestResultReportTests
{
    private const string Repository = "TrentBest/Experiences";
    private const string CandidateSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string WorkflowSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const long RunId = 41234078182;
    private const int RunAttempt = 1;
    private const long JobId = 124634066027;
    private const string JobName = "test-minesweeper";
    private const string JobKey = "test-minesweeper";
    private const string WorkflowPath = ".github/workflows/ci.yml";
    private static readonly DateTimeOffset CreatedAt = DateTimeOffset.Parse("2026-10-31T01:00:00Z");
    private static readonly DateTimeOffset ExpiresAt = DateTimeOffset.Parse("2026-11-07T01:00:00Z");
    private static readonly DateTimeOffset ObservedAt = DateTimeOffset.Parse("2026-10-31T01:05:00Z");
    private static CiRequiredJobPolicy RequiredPolicy => new(new[]
    {
        new CiRequiredJobDefinition(WorkflowPath, JobKey, JobName)
    });
    private static CiRequiredJobPolicy NonRequiredPolicy => new(Array.Empty<CiRequiredJobDefinition>());

    public static int Run()
    {
        var failedTrx = Encoding.UTF8.GetBytes(CreateTrx(
            ("test-1", "Passes", "Passed", null, null),
            ("test-2", "RevealingMineLosesAndTerminalActionsAreRejected", "Failed",
                "Expected: Accepted; Actual: RejectedTerminal", "at Minesweeper.Tests.GameTests.RevealingMineLosesAndTerminalActionsAreRejected() in GameTests.cs:line 42"),
            ("test-3", "ResetRestoresOriginalFixedMineLayoutAfterFirstRevealRelocation", "Failed",
                "Expected: Accepted; Actual: RejectedTerminal", "at Minesweeper.Tests.GameTests.ResetRestoresOriginalFixedMineLayoutAfterFirstRevealRelocation() in GameTests.cs:line 88")));
        var failedPacket = CreatePacket("failure");
        var failedSource = CreateSource(failedTrx);

        Assert(RequiredPolicy.Evaluate(failedPacket, JobId, JobKey) == CiRequiredCheckStatus.Failed &&
            RequiredPolicy.Evaluate(CreatePacket("success"), JobId, JobKey) == CiRequiredCheckStatus.Passed,
            "CiAi required-job policy did not derive the exact observed job outcome");
        Assert(RequiredPolicy.Evaluate(failedPacket, JobId, "optional-job") == CiRequiredCheckStatus.NotRequired,
            "unconfigured workflow job was treated as a required check");
        var otherWorkflowPolicy = new CiRequiredJobPolicy(new[]
        {
            new CiRequiredJobDefinition(".github/workflows/other.yml", JobKey, JobName)
        });
        Assert(otherWorkflowPolicy.Evaluate(failedPacket, JobId, JobKey) == CiRequiredCheckStatus.NotRequired,
            "required job policy was applied across a different workflow definition");
        Assert(RequiredPolicy.Evaluate(failedPacket, JobId + 1, JobKey) == CiRequiredCheckStatus.Incomplete,
            "required-job policy accepted a missing numeric job identity");
        Throws<ArgumentException>(() => new CiRequiredJobPolicy(new[]
        {
            new CiRequiredJobDefinition(JobKey, JobName),
            new CiRequiredJobDefinition(WorkflowPath, JobKey, "duplicate")
        }));
        Throws<ArgumentException>(() => new CiRequiredJobDefinition(WorkflowPath, "../unsafe", JobName));
        var actionable = CiTestResultReport.ParseAndValidate(
            failedTrx, failedPacket, failedSource, RequiredPolicy, ObservedAt);
        Assert(actionable.Verdict == CiTestResultVerdict.ActionableFailure &&
            actionable.Total == 3 && actionable.Passed == 1 && actionable.Failed == 2 &&
            actionable.Skipped == 0 && actionable.Results.Count == 3,
            "multiple failing TRX cases were not normalized with exact counts");
        Assert(actionable.Repository == Repository && actionable.CandidateSha == CandidateSha &&
            actionable.RunId == RunId && actionable.RunAttempt == RunAttempt &&
            actionable.JobId == JobId && actionable.JobName == JobName &&
            actionable.WorkflowPath == WorkflowPath && actionable.WorkflowDefinitionSha == WorkflowSha &&
            actionable.PullRequestNumber == 17 &&
            actionable.PullRequestHeadRef == "feature/minesweeper-domain-pilot" &&
            actionable.PullRequestHeadSha == new string('c', 40) &&
            actionable.PullRequestBaseRef == "development" &&
            actionable.PullRequestBaseSha == new string('d', 40) &&
            actionable.RunEvidenceUrl.AbsolutePath == $"/{Repository}/actions/runs/{RunId}" &&
            actionable.JobEvidenceUrl.AbsolutePath == $"/{Repository}/actions/runs/{RunId}/job/{JobId}",
            "normalized report lost exact workflow, checkout, PR, run, attempt, or job provenance");

        Assert(actionable.RequiredCheckStatus == CiRequiredCheckStatus.Failed &&
            actionable.SourceArtifactId == 7001 && actionable.SourceArtifactName ==
                $"ciai-test-results-{RunId}-{RunAttempt}-{JobKey}" &&
            actionable.SourceJobKey == JobKey && actionable.ProviderArchiveDigest ==
                "sha256:" + new string('a', 64) && actionable.ArchiveSizeBytes == 512 &&
            actionable.ArtifactCreatedAt == CreatedAt && actionable.ArtifactExpiresAt == ExpiresAt &&
            actionable.PayloadSha256?.Length == 64 && actionable.PayloadSizeBytes == failedTrx.Length,
            "report omitted exact artifact identity, provider/payload digests, size, or retention metadata");
        Assert(actionable.Results.Where(item => item.Outcome == CiTestOutcome.Failed)
            .All(item => !string.IsNullOrWhiteSpace(item.Message) &&
                !string.IsNullOrWhiteSpace(item.StackTrace) && item.RecordSha256.Length == 64),
            "failed test identity, assertion, stack trace, or record digest was not retained");
        Assert(actionable.ToJson().Contains(CiTestResultReport.CurrentSchema, StringComparison.Ordinal) &&
            actionable.ToJson().Contains("ActionableFailure", StringComparison.Ordinal),
            "normalized report did not serialize its versioned schema and named verdict");

        var passingTrx = Encoding.UTF8.GetBytes(CreateTrx(
            ("test-1", "FirstPass", "Passed", null, null),
            ("test-2", "SecondPass", "Passed", null, null),
            ("test-3", "SkippedCase", "NotExecuted", null, null),
            total: 3, passed: 2, failed: 0, skipped: 1));
        var passing = CiTestResultReport.ParseAndValidate(
            passingTrx, CreatePacket("success"), CreateSource(passingTrx),
            RequiredPolicy, ObservedAt);
        Assert(passing.Verdict == CiTestResultVerdict.CompletePass &&
            passing.Passed == 2 && passing.Skipped == 1,
            "complete successful test evidence did not produce a pass verdict");

        var noDiagnosticTrx = Encoding.UTF8.GetBytes(CreateTrx(
            ("test-1", "FailureWithoutDiagnostic", "Failed", null, null),
            total: 1, passed: 0, failed: 1, skipped: 0));
        var nonActionable = CiTestResultReport.ParseAndValidate(
            noDiagnosticTrx, CreatePacket("failure"), CreateSource(noDiagnosticTrx),
            RequiredPolicy, ObservedAt);
        Assert(nonActionable.Verdict == CiTestResultVerdict.NonActionableFailure &&
            nonActionable.ReasonCodes.Contains("test-results.failure-diagnostics-incomplete"),
            "failed test without a bounded diagnostic was treated as repairable");

        var stackOnlyText = "at Minesweeper.Tests.GameTests.StackOnlyFailure() in GameTests.cs:line 123";
        var stackOnlyTrx = Encoding.UTF8.GetBytes(CreateTrx(
            ("test-stack", "StackOnlyFailure", "Failed", null, stackOnlyText),
            total: 1, passed: 0, failed: 1, skipped: 0));
        var stackOnly = CiTestResultReport.ParseAndValidate(
            stackOnlyTrx, CreatePacket("failure"), CreateSource(stackOnlyTrx),
            RequiredPolicy, ObservedAt);
        Assert(stackOnly.Results[0].Message is null &&
            stackOnly.Results[0].StackTrace == stackOnlyText,
            "TRX ErrorInfo StackTrace was conflated with assertion message or actual value");

        var noTestFailureButRedJobTrx = Encoding.UTF8.GetBytes(CreateTrx(
            ("test-1", "PassedBeforeBuildFailure", "Passed", null, null),
            total: 1, passed: 1, failed: 0, skipped: 0));
        var jobFailure = CiTestResultReport.ParseAndValidate(
            noTestFailureButRedJobTrx, CreatePacket("failure"), CreateSource(noTestFailureButRedJobTrx),
            RequiredPolicy, ObservedAt);
        Assert(jobFailure.Verdict == CiTestResultVerdict.NonActionableFailure &&
            jobFailure.ReasonCodes.Contains("test-results.check-failed-without-test-failure"),
            "a red job without a failing test was incorrectly classified as an actionable test failure");

        var inconsistentPolicy = new CiRequiredJobPolicy(new[]
        {
            new CiRequiredJobDefinition(WorkflowPath, JobKey, "unexpected-job-name")
        });
        var inconsistent = CiTestResultReport.ParseAndValidate(
            failedTrx, failedPacket, failedSource, inconsistentPolicy, ObservedAt);
        Assert(inconsistent.Verdict == CiTestResultVerdict.IncompleteOrUntrusted &&
            inconsistent.RequiredCheckStatus == CiRequiredCheckStatus.Incomplete &&
            inconsistent.ReasonCodes.Contains("test-results.required-check-incomplete"),
            "job name mismatch in required-check policy was accepted");

        var failedTestsInGreenJob = CiTestResultReport.ParseAndValidate(
            failedTrx, CreatePacket("success"), CreateSource(failedTrx),
            RequiredPolicy, ObservedAt);
        Assert(failedTestsInGreenJob.Verdict == CiTestResultVerdict.IncompleteOrUntrusted &&
            failedTestsInGreenJob.ReasonCodes.Contains("test-results.failed-tests-in-green-job"),
            "failed test records in a green job were accepted as a complete pass");

        var nonRequired = CiTestResultReport.ParseAndValidate(
            passingTrx, CreatePacket("success"), CreateSource(passingTrx),
            NonRequiredPolicy, ObservedAt);
        Assert(nonRequired.Verdict == CiTestResultVerdict.IncompleteOrUntrusted &&
            nonRequired.ReasonCodes.Contains("test-results.check-not-required"),
            "a non-required job was promoted to the required-check pass verdict");

        var missing = CiTestResultReport.MissingArtifact(
            failedPacket, JobId, JobKey, RequiredPolicy, ObservedAt);
        Assert(missing.Verdict == CiTestResultVerdict.IncompleteOrUntrusted &&
            missing.ReasonCodes.Contains("test-results.artifact-missing") &&
            missing.SourceArtifactId is null,
            "missing TRX artifact was not preserved as incomplete evidence");

        var secretTrx = Encoding.UTF8.GetBytes(CreateTrx(
            ("ghp_123456789012345678901234567890", "SecretRedaction", "Failed", "token=ghp_123456789012345678901234567890", "Bearer abcdefghijklmnopqrstuvwxyz0123456789"),
            total: 1, passed: 0, failed: 1, skipped: 0));
        var secretReport = CiTestResultReport.ParseAndValidate(
            secretTrx, CreatePacket("failure"), CreateSource(secretTrx),
            RequiredPolicy, ObservedAt);
        Assert(secretReport.Results[0].SensitiveContentRedacted &&
            secretReport.Results[0].TestId == "[REDACTED]" &&
            !secretReport.ToJson().Contains("ghp_123456789012345678901234567890", StringComparison.Ordinal) &&
            !secretReport.ToJson().Contains("abcdefghijklmnopqrstuvwxyz0123456789", StringComparison.Ordinal),
            "secret-like values were not redacted from normalized diagnostics");

        var duplicateTrx = Encoding.UTF8.GetBytes(CreateTrx(
            ("same", "Duplicate", "Passed", null, null),
            ("same", "Duplicate", "Passed", null, null),
            total: 2, passed: 2, failed: 0, skipped: 0));
        Throws<InvalidDataException>(() => CiTestResultReport.ParseAndValidate(
            duplicateTrx, CreatePacket("success"), CreateSource(duplicateTrx),
            RequiredPolicy, ObservedAt));

        var wrongCountTrx = Encoding.UTF8.GetBytes(CreateTrx(
            ("test-1", "OnlyOne", "Passed", null, null),
            total: 2, passed: 2, failed: 0, skipped: 0));
        Throws<InvalidDataException>(() => CiTestResultReport.ParseAndValidate(
            wrongCountTrx, CreatePacket("success"), CreateSource(wrongCountTrx),
            RequiredPolicy, ObservedAt));

        Throws<InvalidDataException>(() => CiTestResultReport.ParseAndValidate(
            failedTrx, failedPacket, CreateSource(failedTrx, repository: "Other/Repo"),
            RequiredPolicy, ObservedAt));
        Throws<InvalidDataException>(() => CiTestResultReport.ParseAndValidate(
            failedTrx, failedPacket, CreateSource(failedTrx, runAttempt: 2),
            RequiredPolicy, ObservedAt));
        Throws<InvalidDataException>(() => CiTestResultReport.ParseAndValidate(
            failedTrx, failedPacket, CreateSource(failedTrx, jobId: JobId + 1),
            RequiredPolicy, ObservedAt));
        Throws<InvalidDataException>(() => CiTestResultReport.ParseAndValidate(
            failedTrx, failedPacket, CreateSource(failedTrx, jobName: "other-job"),
            RequiredPolicy, ObservedAt));
        Throws<InvalidDataException>(() => CiTestResultReport.ParseAndValidate(
            failedTrx, CreatePacket("failure", workflowTrusted: false), failedSource,
            RequiredPolicy, ObservedAt));
        Throws<InvalidDataException>(() => CiTestResultReport.ParseAndValidate(
            failedTrx, CreatePacket("failure", checkoutMatches: false), failedSource,
            RequiredPolicy, ObservedAt));
        Throws<InvalidDataException>(() => CiTestResultReport.ParseAndValidate(
            failedTrx, failedPacket, failedSource, RequiredPolicy, ExpiresAt.AddSeconds(1)));
        Throws<InvalidDataException>(() => CiTestResultReport.ParseAndValidate(
            failedTrx, failedPacket, CreateSource(failedTrx, payloadSha256: new string('0', 64)),
            RequiredPolicy, ObservedAt));
        Throws<InvalidDataException>(() => CiTestResultReport.ParseAndValidate(
            failedTrx, failedPacket, failedSource, RequiredPolicy, ObservedAt,
            maximumPayloadBytes: 2));
        Throws<ArgumentException>(() => CreateSource(failedTrx, archiveDigest: "not-a-digest"));
        Throws<ArgumentException>(() => CreateSource(failedTrx, artifactName: "wrong-artifact-name"));

        var dtd = Encoding.UTF8.GetBytes("<!DOCTYPE TestRun [<!ENTITY xxe SYSTEM 'file:///etc/passwd'>]><TestRun>&xxe;</TestRun>");
        Throws<XmlException>(() => CiTestResultReport.ParseAndValidate(
            dtd, failedPacket, CreateSource(dtd), RequiredPolicy, ObservedAt));

        var truncatedTrx = Encoding.UTF8.GetBytes(CreateTrx(
            ("test-1", "LongDiagnostic", "Failed", new string('x', 5_000), new string('y', 9_000)),
            total: 1, passed: 0, failed: 1, skipped: 0));
        var truncated = CiTestResultReport.ParseAndValidate(
            truncatedTrx, failedPacket, CreateSource(truncatedTrx),
            RequiredPolicy, ObservedAt);
        Assert(truncated.Results[0].DiagnosticTruncated &&
            truncated.Results[0].Message!.Length <= 4_020 &&
            truncated.Results[0].StackTrace!.Length <= 8_020,
            "diagnostic message or stack trace was not bounded");

        Console.WriteLine("PASS: 35 provenance-bound TRX diagnostic report contract checks.");
        return 35;
    }

    private static CiWorkflowEvidencePacketV3 CreatePacket(
        string conclusion,
        bool workflowTrusted = true,
        bool checkoutMatches = true)
    {
        var eventRef = "refs/pull/17/merge";
        var provenanceJson = $$"""
        {
          "schema": "{{CiWorkflowProvenanceRecord.CurrentSchema}}",
          "repository": "{{Repository}}",
          "runId": {{RunId}},
          "runAttempt": {{RunAttempt}},
          "workflow": {
            "ref": "{{Repository}}/.github/workflows/ci.yml@refs/heads/development",
            "sha": "{{WorkflowSha}}"
          },
          "event": {
            "name": "pull_request",
            "ref": "{{eventRef}}",
            "sha": "{{CandidateSha}}",
            "pullRequest": {
              "number": 17,
              "head": { "ref": "feature/minesweeper-domain-pilot", "sha": "cccccccccccccccccccccccccccccccccccccccc" },
              "base": { "ref": "development", "sha": "dddddddddddddddddddddddddddddddddddddddd" }
            }
          },
          "checkout": {
            "ref": "{{eventRef}}",
            "sha": "{{(checkoutMatches ? CandidateSha : new string('c', 40))}}"
          }
        }
        """;
        var provenanceBytes = Encoding.UTF8.GetBytes(provenanceJson);
        var provenance = CiWorkflowProvenanceRecord.ParseAndValidate(
            provenanceBytes, Repository, RunId, RunAttempt,
            Convert.ToHexString(SHA256.HashData(provenanceBytes)));
        var job = new CiWorkflowJobObservation(
            JobId, RunId, RunAttempt, JobName, "completed", conclusion,
            new Uri($"https://github.com/{Repository}/actions/runs/{RunId}/job/{JobId}"),
            Array.AsReadOnly(new[]
            {
                new CiWorkflowStepObservation(1, "dotnet test", "completed", conclusion,
                    "2026-10-31T01:03:00Z")
            }));
        var observed = new CiWorkflowEvidencePacket(
            Repository, ".github/workflows/ci.yml", RunId, RunAttempt,
            "refs/heads/development", CandidateSha, "pull_request", "Validation",
            CiRunStatus.Completed,
            conclusion == "success" ? CiRunConclusion.Success : CiRunConclusion.Failure,
            new Uri($"https://github.com/{Repository}/actions/runs/{RunId}"),
            ObservedAt, Array.AsReadOnly(new[] { job }), true, true);
        return CiWorkflowEvidencePacketV3.FromValidatedProvenance(
            observed, provenance, workflowTrusted);
    }

    private static CiTestResultArtifactSource CreateSource(
        byte[] payload,
        string? repository = null,
        int? runAttempt = null,
        long? jobId = null,
        string? jobName = null,
        string? artifactName = null,
        string? archiveDigest = null,
        string? payloadSha256 = null)
    {
        var digest = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        return new CiTestResultArtifactSource(
            repository ?? Repository, RunId, runAttempt ?? RunAttempt,
            jobId ?? JobId, jobName ?? JobName, JobKey,
            7001, artifactName ?? $"ciai-test-results-{RunId}-{runAttempt ?? RunAttempt}-{JobKey}",
            archiveDigest ?? "sha256:" + new string('a', 64), 512,
            payloadSha256 ?? digest, payload.Length, "xUnit", CreatedAt, ExpiresAt);
    }

    private static string CreateTrx(
        (string Id, string Name, string Outcome, string? Message, string? Stack) first,
        int? total = null, int? passed = null, int? failed = null, int? skipped = null) =>
        BuildTrx(new[] { first }, total, passed, failed, skipped);

    private static string CreateTrx(
        (string Id, string Name, string Outcome, string? Message, string? Stack) first,
        (string Id, string Name, string Outcome, string? Message, string? Stack) second,
        int? total = null, int? passed = null, int? failed = null, int? skipped = null) =>
        BuildTrx(new[] { first, second }, total, passed, failed, skipped);

    private static string CreateTrx(
        (string Id, string Name, string Outcome, string? Message, string? Stack) first,
        (string Id, string Name, string Outcome, string? Message, string? Stack) second,
        (string Id, string Name, string Outcome, string? Message, string? Stack) third,
        int? total = null, int? passed = null, int? failed = null, int? skipped = null) =>
        BuildTrx(new[] { first, second, third }, total, passed, failed, skipped);

    private static string BuildTrx(
        (string Id, string Name, string Outcome, string? Message, string? Stack)[] results,
        int? total, int? passed, int? failed, int? skipped)
    {
        var actualPassed = results.Count(item => item.Outcome == "Passed");
        var actualFailed = results.Count(item => item.Outcome is "Failed" or "Error" or "Timeout" or "Aborted");
        var actualSkipped = results.Length - actualPassed - actualFailed;
        var xmlResults = string.Join("\n", results.Select(item =>
            $"<UnitTestResult testId=\"{item.Id}\" testName=\"{item.Name}\" outcome=\"{item.Outcome}\">" +
            (item.Message is null && item.Stack is null ? "" :
                "<Output><ErrorInfo>" +
                (item.Message is null ? "" : $"<Message>{System.Security.SecurityElement.Escape(item.Message)}</Message>") +
                (item.Stack is null ? "" : $"<StackTrace>{System.Security.SecurityElement.Escape(item.Stack)}</StackTrace>") +
                "</ErrorInfo></Output>") + "</UnitTestResult>"));
        return $"""
        <TestRun>
          <Results>{xmlResults}</Results>
          <ResultSummary>
            <Counters total="{total ?? results.Length}" executed="{results.Length}"
              passed="{passed ?? actualPassed}" failed="{failed ?? actualFailed}"
              error="0" timeout="0" aborted="0" inconclusive="0"
              notExecuted="{skipped ?? actualSkipped}" notRunnable="0" disconnected="0"
              warning="0" completed="{results.Length}" inProgress="0" pending="0" />
          </ResultSummary>
        </TestRun>
        """;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
