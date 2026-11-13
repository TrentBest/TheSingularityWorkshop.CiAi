using System.IO.Compression;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
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
            new CiRequiredJobDefinition(WorkflowPath, JobKey, JobName),
            new CiRequiredJobDefinition(WorkflowPath, JobKey, "duplicate")
        }));
        Throws<ArgumentException>(() => new CiRequiredJobDefinition(WorkflowPath, "../unsafe", JobName));
        var actionable = CiTestResultReport.ParseAndValidate(
            failedTrx, failedPacket, failedSource, RequiredPolicy, ObservedAt);

        // Exercise the complete trust path: provider ZIP digest -> bounded archive extraction ->
        // embedded provenance digest/identity -> trusted run evidence -> normalized test verdict.
        var embeddedProvenance = CreateProvenanceJson(failedPacket);
        var zipBytes = CreateArtifactArchive(failedTrx, embeddedProvenance);
        var zipDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(zipBytes)).ToLowerInvariant();
        var verifiedArchive = CiTestResultArtifactArchive.Read(
            zipBytes, zipDigest, "results.trx", "provenance.json");
        var archiveSource = CreateSource(failedTrx, archiveBytes: zipBytes);
        var archiveReport = CiTestResultReport.ParseAndValidate(
            verifiedArchive, failedPacket, archiveSource, RequiredPolicy, ObservedAt);
        Assert(archiveReport.Verdict == CiTestResultVerdict.ActionableFailure &&
            archiveReport.Failed == 2 &&
            archiveReport.ProviderArchiveDigest == zipDigest &&
            archiveReport.ArchiveSizeBytes == zipBytes.Length,
            "verified archive did not flow through provenance validation into the authoritative CODE RED verdict");

        // The ZIP container and its individual payloads have different documented limits.
        Throws<InvalidDataException>(() => CreateSource(failedTrx, archiveSizeBytes:
            CiTestResultArtifactArchive.DefaultMaximumPayloadBytes + 1));
        Throws<ArgumentOutOfRangeException>(() => CreateSource(failedTrx, archiveSizeBytes:
            CiTestResultArtifactArchive.DefaultMaximumArchiveBytes + 1));

        var mutatedTrxArchive = CiTestResultArtifactArchive.Read(
            zipBytes, zipDigest, "results.trx", "provenance.json");
        MutateExposedMemory(mutatedTrxArchive.TrxBytes);
        Throws<InvalidDataException>(() => CiTestResultReport.ParseAndValidate(
            mutatedTrxArchive, failedPacket, archiveSource, RequiredPolicy, ObservedAt));

        var mutatedProvenanceArchive = CiTestResultArtifactArchive.Read(
            zipBytes, zipDigest, "results.trx", "provenance.json");
        MutateExposedMemory(mutatedProvenanceArchive.ProvenanceBytes);
        Throws<InvalidDataException>(() => CiTestResultReport.ParseAndValidate(
            mutatedProvenanceArchive, failedPacket, archiveSource, RequiredPolicy, ObservedAt));

        var foreignRunProvenance = CreateProvenanceJson(failedPacket, runId: RunId + 1);
        var foreignRunZip = CreateArtifactArchive(failedTrx, foreignRunProvenance);
        var foreignRunDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(foreignRunZip)).ToLowerInvariant();
        Throws<InvalidDataException>(() => CreateSource(failedTrx, archiveBytes: foreignRunZip));

        var foreignCheckoutProvenance = CreateProvenanceJson(
            failedPacket, checkoutSha: new string('f', 40));
        var foreignCheckoutZip = CreateArtifactArchive(failedTrx, foreignCheckoutProvenance);
        var foreignCheckoutDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(foreignCheckoutZip)).ToLowerInvariant();
        Throws<InvalidDataException>(() => CreateSource(failedTrx, archiveBytes: foreignCheckoutZip));

        Throws<InvalidDataException>(() => CreateSource(failedTrx,
            archiveDigest: "sha256:" + new string('f', 64), archiveBytes: zipBytes));
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
                failedSource.ProviderArchiveDigest && actionable.ArchiveSizeBytes == failedSource.ArchiveSizeBytes &&
            actionable.ArtifactCreatedAt == CreatedAt && actionable.ArtifactExpiresAt == ExpiresAt &&
            actionable.PayloadSha256 == failedSource.PayloadSha256 && actionable.PayloadSizeBytes == failedTrx.Length,
            "report omitted exact artifact identity, provider/payload digests, size, or retention metadata");
        Assert(actionable.Results.Where(item => item.Outcome == CiTestOutcome.Failed)
            .All(item => !string.IsNullOrWhiteSpace(item.Message) &&
                !string.IsNullOrWhiteSpace(item.StackTrace) && item.RecordSha256.Length == 64),
            "failed test identity, assertion, stack trace, or record digest was not retained");
        Assert(actionable.ToJson().Contains(CiTestResultReport.CurrentSchema, StringComparison.Ordinal) &&
            actionable.ToJson().Contains("ActionableFailure", StringComparison.Ordinal),
            "normalized report did not serialize its versioned schema and named verdict");

        var canonicalBytes = actionable.ToCanonicalUtf8Bytes();
        var canonicalJson = Encoding.UTF8.GetString(canonicalBytes);
        var canonicalDigest = Convert.ToHexString(SHA256.HashData(canonicalBytes)).ToLowerInvariant();
        Assert(canonicalBytes.Length <= CiTestResultReport.DefaultMaximumReportBytes &&
            canonicalBytes.Length > 0 && canonicalBytes[0] != 0xEF &&
            canonicalBytes[^1] != (byte)'\n' && canonicalBytes[^1] != (byte)'\r',
            "canonical report violated its bounded UTF-8/no-BOM/no-trailing-newline contract");
        Assert(canonicalJson.StartsWith("{\"contractVersion\":1,\"schema\":\"ciai.test-results.v1\"",
                StringComparison.Ordinal) &&
            canonicalJson.Contains("\"verdict\":\"ActionableFailure\"", StringComparison.Ordinal) &&
            !canonicalJson.Contains("ReportIdentity", StringComparison.Ordinal) &&
            !canonicalJson.Contains("reportIdentity", StringComparison.Ordinal),
            "canonical report property order, schema, enum encoding, or identity exclusion changed");
        Assert(actionable.ReportIdentity == "sha256:" + canonicalDigest &&
            actionable.ReportSha256 == canonicalDigest &&
            actionable.ToCanonicalUtf8Bytes().SequenceEqual(canonicalBytes),
            "report identity was not the deterministic SHA-256 of the exact canonical bytes");

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

        var unknownOutcomeTrx = Encoding.UTF8.GetBytes(CreateTrx(
            ("test-unknown", "FutureOutcome", "Queued", null, null),
            total: 1, passed: 0, failed: 0, skipped: 0));
        var unknownOutcome = CiTestResultReport.ParseAndValidate(
            unknownOutcomeTrx, CreatePacket("failure"), CreateSource(unknownOutcomeTrx),
            RequiredPolicy, ObservedAt);
        Assert(unknownOutcome.Other == 1 && unknownOutcome.Results[0].Outcome == CiTestOutcome.Other &&
            unknownOutcome.Results[0].RawOutcome == "Queued" &&
            unknownOutcome.Verdict == CiTestResultVerdict.IncompleteOrUntrusted &&
            unknownOutcome.ReasonCodes.Contains("test-results.unknown-outcome"),
            "unknown TRX outcome was not preserved as explicit non-actionable evidence");

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
        Assert(failedSource.PayloadSha256 ==
            Convert.ToHexString(SHA256.HashData(failedTrx)).ToLowerInvariant() &&
            failedSource.PayloadSizeBytes == failedTrx.Length,
            "source payload identity was not derived from the verified archive bytes");
        Throws<InvalidDataException>(() => CiTestResultReport.ParseAndValidate(
            failedTrx, failedPacket, failedSource, RequiredPolicy, ObservedAt,
            maximumPayloadBytes: 2));
        Throws<ArgumentException>(() => CreateSource(failedTrx, archiveDigest: "not-a-digest"));
        Assert(CreateSource(failedTrx, artifactName: "minesweeper-test-results").ArtifactName ==
            "minesweeper-test-results",
            "trusted provider artifact name was incorrectly required to follow a CiAi-generated naming convention");
        Throws<ArgumentException>(() => CreateSource(failedTrx, artifactName: ""));

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

        Console.WriteLine("PASS: 36 provenance-bound TRX diagnostic report contract checks.");
        return 36;
    }

    internal static CiTestResultReport CreateSampleReport()
    {
        var trx = Encoding.UTF8.GetBytes(CreateTrx(
            ("minesweeper-1", "Minesweeper.Domain", "Failed", "Expected a safe cell.", "System.InvalidOperationException: test diagnostic"),
            total: 1, passed: 0, failed: 1, skipped: 0));
        return CiTestResultReport.ParseAndValidate(
            trx, CreatePacket("failure"), CreateSource(trx, artifactName: "minesweeper-test-results"),
            RequiredPolicy, ObservedAt);
    }

    private static CiWorkflowEvidencePacket CreatePacket(
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
        var observed = new CiWorkflowRunEvidenceObservation(
            Repository, ".github/workflows/ci.yml", RunId, RunAttempt,
            "refs/heads/development", CandidateSha, "pull_request", "Validation",
            CiRunStatus.Completed,
            conclusion == "success" ? CiRunConclusion.Success : CiRunConclusion.Failure,
            new Uri($"https://github.com/{Repository}/actions/runs/{RunId}"),
            ObservedAt, Array.AsReadOnly(new[] { job }), true, true);
        return CiWorkflowEvidencePacket.FromValidatedProvenance(
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
        long? archiveSizeBytes = null,
        byte[]? archiveBytes = null)
    {
        var evidence = CreatePacket("failure");
        var exactArchiveBytes = archiveBytes ?? CreateArtifactArchive(payload, CreateProvenanceJson(evidence));
        var actualDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(exactArchiveBytes)).ToLowerInvariant();
        var binding = new CiTestResultProviderArtifactBinding(
            repository ?? Repository, RunId, runAttempt ?? RunAttempt,
            jobId ?? JobId, jobName ?? JobName, JobKey,
            7001, artifactName ?? $"ciai-test-results-{RunId}-{runAttempt ?? RunAttempt}-{JobKey}",
            archiveDigest ?? actualDigest, archiveSizeBytes ?? exactArchiveBytes.Length,
            CreatedAt, ExpiresAt, isExpired: false);
        var archive = CiTestResultArtifactArchive.Read(
            exactArchiveBytes, binding, "results.trx", "provenance.json");
        return CiTestResultArtifactSource.FromVerifiedProviderObservation(
            evidence, RequiredPolicy, binding, archive, "xUnit");
    }

    private static void MutateExposedMemory(ReadOnlyMemory<byte> memory)
    {
        if (!MemoryMarshal.TryGetArray(memory, out var segment) || segment.Array is null || segment.Count == 0)
            throw new InvalidOperationException("Test could not access the expected array-backed memory.");
        segment.Array[segment.Offset] ^= 0x01;
    }

    private static byte[] CreateProvenanceJson(
        CiWorkflowEvidencePacket evidence,
        long? runId = null,
        string? checkoutSha = null)
    {
        var provenance = evidence.Provenance;
        var pullRequest = provenance.PullRequest is null ? null : new
        {
            number = provenance.PullRequest.Number,
            head = new { @ref = provenance.PullRequest.HeadRef, sha = provenance.PullRequest.HeadSha },
            @base = new { @ref = provenance.PullRequest.BaseRef, sha = provenance.PullRequest.BaseSha }
        };
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = CiWorkflowProvenanceRecord.CurrentSchema,
            repository = provenance.Repository,
            runId = runId ?? provenance.RunId,
            runAttempt = provenance.RunAttempt,
            workflow = new { @ref = provenance.WorkflowRef, sha = provenance.WorkflowSha },
            @event = new
            {
                name = provenance.EventName,
                @ref = provenance.EventRef,
                sha = provenance.EventSha,
                pullRequest
            },
            checkout = new { @ref = provenance.CheckoutRef, sha = checkoutSha ?? provenance.CheckoutSha }
        });
    }

    private static byte[] CreateArtifactArchive(byte[] trx, byte[] provenance)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var trxEntry = archive.CreateEntry("results.trx", CompressionLevel.Optimal);
            using (var stream = trxEntry.Open()) stream.Write(trx);
            var provenanceEntry = archive.CreateEntry("provenance.json", CompressionLevel.Optimal);
            using (var stream = provenanceEntry.Open()) stream.Write(provenance);
        }
        return output.ToArray();
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
        var actualSkipped = results.Count(item => item.Outcome is "NotExecuted" or "Inconclusive" or
            "NotRunnable" or "Disconnected" or "Warning" or "Pending" or "InProgress" or "PassedButRunAborted");
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
