using System.Security.Cryptography;
using TheSingularityWorkshop.CiAi;

var passed = 0;
passed += CiPipelineDefinitionContractTests.Run();
passed += NuGetPackageArtifactVerifierContractTests.Run();
Check("valid completed success is authoritative", () =>
{
    var run = NewRun();
    Assert(run.IsSuccessful, "successful run was not recognized");
    Assert(run.Identity == "TrentBest/SimpleDemos#42", "run identity is not stable");
});
Check("in-progress run cannot claim a final conclusion", () =>
{
    Throws<ArgumentException>(() => NewRun(status: CiRunStatus.InProgress, conclusion: CiRunConclusion.Success));
});
Check("completed run requires a conclusion", () =>
{
    Throws<ArgumentException>(() => NewRun(status: CiRunStatus.Completed, conclusion: CiRunConclusion.None));
});
Check("repository identity must be owner/repository", () =>
{
    Throws<ArgumentException>(() => NewRun(repository: "SimpleDemos"));
});
Check("evidence must be an HTTPS GitHub Actions run URL", () =>
{
    Throws<ArgumentException>(() => NewRun(url: new Uri("https://example.com/run/42")));
});
Check("evidence must identify the exact requested run", () =>
{
    Throws<ArgumentException>(() => NewRun(url: new Uri("https://github.com/TrentBest/SimpleDemos/actions/runs/43")));
});
Check("commit SHA must be a full 40-character hexadecimal identifier", () =>
{
    Throws<ArgumentException>(() => NewRun(headSha: "abcdef0123456789"));
});
Check("evidence URLs reject query strings and fragments", () =>
{
    Throws<ArgumentException>(() => NewRun(url: new Uri("https://github.com/TrentBest/SimpleDemos/actions/runs/42?redirect=elsewhere")));
});
Check("repository names reject path and URL syntax", () =>
{
    Throws<ArgumentException>(() => NewRun(repository: "TrentBest/../Other"));
});
Check("unknown provider conclusion is not treated as success", () =>
{
    var run = NewRun(conclusion: CiRunConclusion.Unknown);
    Assert(!run.IsSuccessful, "unknown conclusion was treated as success");
});

Check("evidence verifier accepts exact successful observation", () =>
{
    var errors = CiRunEvidenceVerifier.VerifySuccessfulRun(
        NewRun(), "TrentBest/SimpleDemos", 42, "abcdef0123456789abcdef0123456789abcdef01");
    Assert(errors.Count == 0, "exact successful run should reconcile");
});
Check("evidence verifier rejects a different target repository", () =>
{
    var errors = CiRunEvidenceVerifier.VerifySuccessfulRun(
        NewRun(), "TrentBest/OtherRepo", 42, "abcdef0123456789abcdef0123456789abcdef01");
    Assert(errors.Contains("repository.mismatch"), "wrong repository was not rejected");
});
Check("evidence verifier rejects a different run ID", () =>
{
    var errors = CiRunEvidenceVerifier.VerifySuccessfulRun(
        NewRun(), "TrentBest/SimpleDemos", 43, "abcdef0123456789abcdef0123456789abcdef01");
    Assert(errors.Contains("runId.mismatch"), "wrong run ID was not rejected");
});
Check("evidence verifier rejects a different commit SHA", () =>
{
    var errors = CiRunEvidenceVerifier.VerifySuccessfulRun(
        NewRun(), "TrentBest/SimpleDemos", 42, "123456789abcdef0123456789abcdef012345678");
    Assert(errors.Contains("headSha.mismatch"), "wrong commit SHA was not rejected");
});
Check("evidence verifier rejects a failed run", () =>
{
    var errors = CiRunEvidenceVerifier.VerifySuccessfulRun(
        NewRun(conclusion: CiRunConclusion.Failure), "TrentBest/SimpleDemos", 42,
        "abcdef0123456789abcdef0123456789abcdef01");
    Assert(errors.Contains("run.not-successful"), "failed run was not rejected");
});

Check("artifact manifest normalizes digest and preserves immutable identity", () =>
{
    var manifest = NewManifest(digest: new string('A', 64));
    Assert(manifest.Digest == new string('a', 64), "digest was not normalized");
    Assert(manifest.RequestId == "request-1" && manifest.ArtifactVersion == "v1", "manifest identity was not preserved");
});
Check("artifact manifest rejects malformed SHA-256 digests", () =>
{
    Throws<ArgumentException>(() => NewManifest(digest: "not-a-digest"));
});
Check("artifact manifest rejects non-HTTPS retrieval references", () =>
{
    Throws<ArgumentException>(() => NewManifest(uri: new Uri("http://provider.example/artifacts/1")));
});
Check("artifact manifest requires expiry after creation", () =>
{
    Throws<ArgumentException>(() => NewManifest(expiresAt: DateTimeOffset.Parse("2026-10-24T00:00:00Z")));
});
Check("ready notice must target the manifest client", () =>
{
    Throws<ArgumentException>(() => new ArtifactReadyNotice(1, "notice-1", "other-client",
        NewManifest(), DateTimeOffset.Parse("2026-10-24T00:05:00Z")));
});
Check("ready notice rejects unsupported contract versions", () =>
{
    Throws<ArgumentOutOfRangeException>(() => new ArtifactReadyNotice(2, "notice-1", "client-ci",
        NewManifest(), DateTimeOffset.Parse("2026-10-24T00:05:00Z")));
});
Check("receipt cannot claim integrity verification before retrieval", () =>
{
    Throws<ArgumentException>(() => NewAcknowledgement(retrieved: false, integrityVerified: true));
});
Check("valid receipt makes payload eligible for policy review, not deletion", () =>
{
    var receipt = NewAcknowledgement(retrieved: true, integrityVerified: true);
    Assert(receipt.MakesPayloadEligibleForRetentionReview, "verified receipt should permit retention review");
});
Check("rejected local validation blocks retention eligibility", () =>
{
    var receipt = NewAcknowledgement(retrieved: true, integrityVerified: true,
        localValidation: ArtifactLocalValidationOutcome.Rejected);
    Assert(!receipt.MakesPayloadEligibleForRetentionReview, "rejected artifact became eligible for cleanup review");
});
Check("artifact manifest rejects missing digest instead of throwing null reference", () =>
{
    Throws<ArgumentException>(() => NewManifest(digest: null!));
});
Check("receipt reconciles with its exact manifest", () =>
{
    var receipt = NewAcknowledgement();
    Assert(receipt.VerifyAgainst(NewManifest()).Count == 0, "matching receipt should reconcile");
});
Check("receipt rejects mismatched artifact version and digest", () =>
{
    var receipt = NewAcknowledgement();
    var errors = receipt.VerifyAgainst(NewManifest(digest: new string('b', 64)));
    Assert(errors.Contains("digest.mismatch"), "wrong digest was not rejected");
});
Check("receipt rejects a missing manifest", () =>
{
    Assert(NewAcknowledgement().VerifyAgainst(null).Contains("manifest.required"),
        "missing manifest was not reported");
});
Check("receipt is correlated to an exact artifact version and digest", () =>
{
    var receipt = NewAcknowledgement();
    Assert(receipt.RequestId == "request-1" && receipt.ArtifactId == "artifact-1" &&
        receipt.ArtifactVersion == "v1" && receipt.Digest == new string('a', 64),
        "receipt correlation fields were not preserved");
});
Check("receipt reconciles exact provider repository, run, and source revision", () =>
{
    var receipt = NewAcknowledgement();
    Assert(receipt.VerifyAgainst(NewManifest()).Count == 0, "matching provider provenance should reconcile");
});
Check("receipt rejects provider repository mismatch", () =>
{
    var errors = NewAcknowledgement().VerifyAgainst(NewManifest(providerRepository: "Other/Provider"));
    Assert(errors.Contains("providerRepository.mismatch"), "wrong provider repository was not rejected");
});
Check("receipt rejects provider run mismatch", () =>
{
    var errors = NewAcknowledgement().VerifyAgainst(NewManifest(providerRunId: 18));
    Assert(errors.Contains("providerRunId.mismatch"), "wrong provider run was not rejected");
});
Check("receipt rejects source revision mismatch", () =>
{
    var errors = NewAcknowledgement().VerifyAgainst(NewManifest(sourceSha: "123456789abcdef0123456789abcdef012345678"));
    Assert(errors.Contains("sourceSha.mismatch"), "wrong source revision was not rejected");
});
Check("receipt cannot reconcile provider run without provenance", () =>
{
    var receipt = new ArtifactReceiptAcknowledgement(1, "ack-1", "request-1:artifact-1:v1",
        "request-1", "artifact-1", "v1", new string('a', 64), "client-ci", true, true,
        DateTimeOffset.Parse("2026-10-24T00:10:00Z"));
    var errors = receipt.VerifyAgainst(NewManifest());
    Assert(errors.Contains("providerRepository.required") && errors.Contains("providerRunId.required") &&
        errors.Contains("sourceSha.required"), "missing provider provenance was not rejected");
});
Check("receipt ledger records once and suppresses retry after reopening", () =>
{
    var directory = Path.Combine(Path.GetTempPath(), "ciai-receipts-" + Guid.NewGuid().ToString("N"));
    var path = Path.Combine(directory, "receipts.log");
    try
    {
        var receipt = NewAcknowledgement();
        var manifest = NewManifest();
        Assert(new FileArtifactReceiptLedger(path).Record(receipt, manifest) == ArtifactReceiptRecordOutcome.Recorded,
            "first receipt was not recorded");
        Assert(new FileArtifactReceiptLedger(path).Record(receipt, manifest) == ArtifactReceiptRecordOutcome.Duplicate,
            "same receipt retry was not deduplicated");
        Assert(new FileArtifactReceiptLedger(path).Record(receipt, manifest) == ArtifactReceiptRecordOutcome.Duplicate,
            "receipt was not deduplicated after reopening the ledger");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
});
Check("receipt ledger rejects conflicting reuse of a deduplication key", () =>
{
    var directory = Path.Combine(Path.GetTempPath(), "ciai-receipts-" + Guid.NewGuid().ToString("N"));
    var path = Path.Combine(directory, "receipts.log");
    try
    {
        var ledger = new FileArtifactReceiptLedger(path);
        Assert(ledger.Record(NewAcknowledgement(), NewManifest()) == ArtifactReceiptRecordOutcome.Recorded,
            "initial receipt was not recorded");
        Assert(ledger.Record(NewAcknowledgement(acknowledgementId: "ack-conflict"), NewManifest()) ==
            ArtifactReceiptRecordOutcome.Conflict, "conflicting duplicate key was not rejected");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
});
Check("receipt ledger refuses receipts with mismatched provider evidence", () =>
{
    var directory = Path.Combine(Path.GetTempPath(), "ciai-receipts-" + Guid.NewGuid().ToString("N"));
    var path = Path.Combine(directory, "receipts.log");
    try
    {
        var outcome = new FileArtifactReceiptLedger(path).Record(
            NewAcknowledgement(providerRepository: "Other/Provider"), NewManifest());
        Assert(outcome == ArtifactReceiptRecordOutcome.Rejected, "mismatched provider evidence was accepted");
        Assert(!File.Exists(path), "rejected receipt was persisted");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
});

Check("payload verifier accepts bytes matching manifest size and SHA-256", () =>
{
    var payload = Enumerable.Range(0, 128).Select(i => (byte)i).ToArray();
    var manifest = NewManifestForPayload(payload);
    var result = ArtifactPayloadVerifier.Verify(manifest, payload);
    Assert(result.IsVerified && result.FailureCode is null, "matching payload was rejected");
    Assert(result.ActualSizeBytes == payload.Length && result.ActualDigest == manifest.Digest,
        "verification evidence was not returned");
});
Check("payload verifier rejects a digest mismatch", () =>
{
    var payload = Enumerable.Range(0, 128).Select(i => (byte)i).ToArray();
    var result = ArtifactPayloadVerifier.Verify(NewManifest(), payload);
    Assert(!result.IsVerified && result.FailureCode == "payload.digest-mismatch",
        "payload with a different digest was not rejected");
});
Check("payload verifier rejects a size mismatch before hashing", () =>
{
    var payload = new byte[127];
    var result = ArtifactPayloadVerifier.Verify(NewManifest(), payload);
    Assert(!result.IsVerified && result.FailureCode == "payload.size-mismatch",
        "payload with wrong size was not rejected");
});
Check("payload verifier enforces caller-provided maximum size", () =>
{
    var payload = new byte[128];
    var result = ArtifactPayloadVerifier.Verify(NewManifest(), payload, maximumPayloadBytes: 64);
    Assert(!result.IsVerified && result.FailureCode == "payload.exceeds-limit",
        "oversized payload was not rejected");
});
Check("payload verifier rejects a negative maximum size", () =>
{
    Throws<ArgumentOutOfRangeException>(() => ArtifactPayloadVerifier.Verify(NewManifest(), Array.Empty<byte>(), -1));
});

await CheckAsync("bounded stream reader verifies exact payload without taking stream ownership", async () =>
{
    var payload = Enumerable.Range(0, 128).Select(i => (byte)i).ToArray();
    var stream = new MemoryStream(payload);
    var result = await ArtifactPayloadReader.ReadAndVerifyAsync(stream, NewManifestForPayload(payload));
    Assert(result.Verification.IsVerified, "valid streamed payload was rejected");
    Assert(result.Payload.HasValue && result.Payload.Value.Span.SequenceEqual(payload), "verified payload bytes differ");
    Assert(stream.CanRead, "reader unexpectedly disposed caller-owned stream");
});
await CheckAsync("bounded stream reader stops and rejects data beyond configured maximum", async () =>
{
    var payload = new byte[1024];
    var result = await ArtifactPayloadReader.ReadAndVerifyAsync(
        new MemoryStream(payload), NewManifestForPayload(payload), maximumPayloadBytes: 128);
    Assert(!result.Verification.IsVerified && result.Verification.FailureCode == "payload.exceeds-limit",
        "oversized stream was not rejected at the read boundary");
    Assert(!result.Payload.HasValue, "rejected payload bytes were exposed");
});
await CheckAsync("bounded stream reader reports short streams as size mismatch", async () =>
{
    var payload = new byte[128];
    var result = await ArtifactPayloadReader.ReadAndVerifyAsync(
        new MemoryStream(new byte[127]), NewManifestForPayload(payload));
    Assert(!result.Verification.IsVerified && result.Verification.FailureCode == "payload.size-mismatch",
        "short stream was not rejected");
});
await CheckAsync("bounded stream reader validates arguments before reading", async () =>
{
    await ThrowsAsync<ArgumentOutOfRangeException>(() =>
        ArtifactPayloadReader.ReadAndVerifyAsync(new MemoryStream(), NewManifest(), maximumPayloadBytes: -1));
});


Check("retrieval destination policy allows only configured HTTPS hosts", () =>
{
    var policy = new ArtifactRetrievalDestinationPolicy(new[] { "provider.example", "cdn.provider.example" });
    Assert(policy.Validate(new Uri("https://provider.example/artifacts/a"), DateTimeOffset.Parse("2026-10-24T00:30:00Z"),
        DateTimeOffset.Parse("2026-10-24T01:00:00Z")).Count == 0, "allowlisted HTTPS host should be accepted");
    Assert(policy.Validate(new Uri("https://evil.example/artifacts/a"), DateTimeOffset.Parse("2026-10-24T00:30:00Z"),
        DateTimeOffset.Parse("2026-10-24T01:00:00Z")).Contains("retrieval.host-not-allowed"), "unlisted host was accepted");
});
Check("retrieval destination policy rejects non-HTTPS and non-default ports", () =>
{
    var policy = new ArtifactRetrievalDestinationPolicy(new[] { "provider.example" });
    Assert(policy.Validate(new Uri("http://provider.example/artifacts/a"), DateTimeOffset.Parse("2026-10-24T00:30:00Z"),
        DateTimeOffset.Parse("2026-10-24T01:00:00Z")).Contains("retrieval.https-required"), "HTTP was accepted");
    Assert(policy.Validate(new Uri("https://provider.example:8443/artifacts/a"), DateTimeOffset.Parse("2026-10-24T00:30:00Z"),
        DateTimeOffset.Parse("2026-10-24T01:00:00Z")).Contains("retrieval.port-not-allowed"), "non-default port was accepted");
});
Check("retrieval destination policy rejects expired artifacts and IP literals", () =>
{
    var policy = new ArtifactRetrievalDestinationPolicy(new[] { "provider.example" });
    Assert(policy.Validate(new Uri("https://provider.example/a"), DateTimeOffset.Parse("2026-10-24T01:00:00Z"),
        DateTimeOffset.Parse("2026-10-24T01:00:00Z")).Contains("retrieval.expired"), "expired artifact was accepted");
    Assert(policy.Validate(new Uri("https://127.0.0.1/a"), DateTimeOffset.Parse("2026-10-24T00:30:00Z"),
        DateTimeOffset.Parse("2026-10-24T01:00:00Z")).Contains("retrieval.ip-literal-rejected"), "IP literal was accepted");
    Assert(policy.Validate(new Uri("https://provider.example./a"), DateTimeOffset.Parse("2026-10-24T00:30:00Z"),
        DateTimeOffset.Parse("2026-10-24T01:00:00Z")).Contains("retrieval.host-not-allowed"), "trailing-dot hostname bypassed allowlist");
});

Check("retrieval destination policy rejects embedded credentials and fragments", () =>
{
    var policy = new ArtifactRetrievalDestinationPolicy(new[] { "provider.example" });
    var now = DateTimeOffset.Parse("2026-10-24T00:30:00Z");
    var expiry = DateTimeOffset.Parse("2026-10-24T01:00:00Z");
    Assert(policy.Validate(new Uri("https://user:secret@provider.example/artifacts/a"), now, expiry)
        .Contains("retrieval.user-info-rejected"), "embedded credentials were accepted");
    Assert(policy.Validate(new Uri("https://provider.example/artifacts/a#redirect"), now, expiry)
        .Contains("retrieval.fragment-rejected"), "fragment-bearing destination was accepted");
});

Check("retrieval destination policy constructor rejects empty and malformed allowlists", () =>
{
    Throws<ArgumentException>(() => new ArtifactRetrievalDestinationPolicy(Array.Empty<string>()));
    Throws<ArgumentException>(() => new ArtifactRetrievalDestinationPolicy(new[] { "provider.example/path" }));
    Throws<ArgumentException>(() => new ArtifactRetrievalDestinationPolicy(new[] { "127.0.0.1" }));
});


Check("release evidence preserves raw facts and normalizes artifact digest", () =>
{
    var record = NewReleaseEvidence();
    Assert(record.ArtifactDigest == new string('a', 64), "artifact digest was not normalized");
    Assert(record.RawStatus == "completed" && record.RawConclusion == "success",
        "raw provider status/conclusion were not preserved");
});
Check("release evidence rejects an unverified pass", () =>
{
    Throws<ArgumentException>(() => NewReleaseEvidence(directlyVerified: false));
});
Check("release evidence requires an explicit digest scope", () =>
{
    Throws<ArgumentException>(() => NewReleaseEvidence(artifactDigestScope: null));
});
Check("release evidence report passes only complete exact-SHA requirements", () =>
{
    var report = ReleaseEvidenceReport.Aggregate("TrentBest/SimpleDemos",
        "abcdef0123456789abcdef0123456789abcdef01",
        new[] { ReleaseEvidenceCategory.BuildAndTests, ReleaseEvidenceCategory.Security },
        new[] { NewReleaseEvidence(), NewReleaseEvidence(category: ReleaseEvidenceCategory.Security) });
    Assert(report.IsPassing, "fully verified required evidence did not pass");
});
Check("release evidence report marks absent requirement missing", () =>
{
    var report = ReleaseEvidenceReport.Aggregate("TrentBest/SimpleDemos",
        "abcdef0123456789abcdef0123456789abcdef01",
        new[] { ReleaseEvidenceCategory.BuildAndTests, ReleaseEvidenceCategory.Security },
        new[] { NewReleaseEvidence() });
    Assert(!report.IsPassing && report.Categories[ReleaseEvidenceCategory.Security] == ReleaseEvidenceState.Missing,
        "missing security evidence was not kept non-passing");
});
Check("release evidence report rejects mixing another candidate SHA", () =>
{
    Throws<ArgumentException>(() => ReleaseEvidenceReport.Aggregate("TrentBest/SimpleDemos",
        "abcdef0123456789abcdef0123456789abcdef01",
        new[] { ReleaseEvidenceCategory.BuildAndTests },
        new[] { NewReleaseEvidence(headSha: "123456789abcdef0123456789abcdef012345678") }));
});
Check("skipped release evidence cannot make a category pass", () =>
{
    var report = ReleaseEvidenceReport.Aggregate("TrentBest/SimpleDemos",
        "abcdef0123456789abcdef0123456789abcdef01",
        new[] { ReleaseEvidenceCategory.PublicationGate },
        new[] { NewReleaseEvidence(category: ReleaseEvidenceCategory.PublicationGate,
            state: ReleaseEvidenceState.Skipped, rawStatus: "completed", rawConclusion: "skipped") });
    Assert(!report.IsPassing && report.Categories[ReleaseEvidenceCategory.PublicationGate] == ReleaseEvidenceState.Skipped,
        "skipped publication was treated as passing");
});

Check("commit observation Markdown preserves incompleteness and exact identity", () =>
{
    const string observation = """
    {
      "schema": "ciai.commit-observation.v1",
      "reportType": "read-only-workflow-observation",
      "repository": "TrentBest/SimpleDemos",
      "headSha": "abcdef0123456789abcdef0123456789abcdef01",
      "retrievedAtUtc": "2026-10-24T06:00:00+00:00",
      "discovery": { "PagesRead": 1, "TotalCount": 0, "IsTruncated": false, "returnedRuns": 0 },
      "complete": false,
      "note": "observation only",
      "runs": []
    }
    """;
    var markdown = CommitObservationMarkdownRenderer.Render(observation);
    Assert(markdown.Contains("`TrentBest/SimpleDemos`", StringComparison.Ordinal),
        "observation Markdown omitted repository identity");
    Assert(markdown.Contains("`abcdef0123456789abcdef0123456789abcdef01`", StringComparison.Ordinal),
        "observation Markdown omitted full candidate SHA");
    Assert(markdown.Contains("Observation complete:** no", StringComparison.Ordinal),
        "incomplete observation was not shown as incomplete");
    Assert(markdown.Contains("No matching workflow runs were observed.", StringComparison.Ordinal),
        "empty observation was not explained");
    Assert(markdown.Contains("does not certify release readiness", StringComparison.Ordinal),
        "observation report failed to distinguish evidence from release certification");
});
Check("commit observation Markdown rejects unsupported schema and unsafe evidence URLs", () =>
{
    Throws<InvalidDataException>(() => CommitObservationMarkdownRenderer.Render("{\"schema\":\"other\"}"));
    const string unsafeObservation = """
    {
      "schema": "ciai.commit-observation.v1",
      "repository": "TrentBest/SimpleDemos",
      "headSha": "abcdef0123456789abcdef0123456789abcdef01",
      "retrievedAtUtc": "2026-10-24T06:00:00+00:00",
      "discovery": { "PagesRead": 1, "TotalCount": 1, "IsTruncated": false, "returnedRuns": 1 },
      "complete": true,
      "runs": [{
        "run": { "WorkflowName": "verify.yml", "RunId": 42, "RunAttempt": 1, "Event": "push",
          "normalizedStatus": "completed", "normalizedConclusion": "success",
          "WorkflowPath": ".github/workflows/verify.yml", "evidenceUrl": "https://example.com/forged" },
        "jobs": [], "jobsComplete": true
      }]
    }
    """;
    Throws<InvalidDataException>(() => CommitObservationMarkdownRenderer.Render(unsafeObservation));
});

Check("Markdown release report is deterministic and includes exact candidate identity", () =>
{
    var report = ReleaseEvidenceReport.Aggregate("TrentBest/SimpleDemos",
        "abcdef0123456789abcdef0123456789abcdef01",
        new[] { ReleaseEvidenceCategory.BuildAndTests, ReleaseEvidenceCategory.Security },
        new[] { NewReleaseEvidence(), NewReleaseEvidence(category: ReleaseEvidenceCategory.Security) });
    var first = ReleaseEvidenceMarkdownRenderer.Render(report);
    var second = ReleaseEvidenceMarkdownRenderer.Render(report);
    Assert(first == second, "same report produced different Markdown");
    Assert(first.Contains("`TrentBest/SimpleDemos`", StringComparison.Ordinal) &&
        first.Contains("`abcdef0123456789abcdef0123456789abcdef01`", StringComparison.Ordinal),
        "report omitted exact repository or candidate SHA");
    Assert(first.Contains("BuildAndTests", StringComparison.Ordinal) &&
        first.Contains("Security", StringComparison.Ordinal), "report omitted gate categories");
    Assert(first.Contains("not release authorization", StringComparison.Ordinal),
        "report failed to distinguish evidence from authorization");
});
Check("Markdown release report preserves missing and skipped gates without upgrading them", () =>
{
    var report = ReleaseEvidenceReport.Aggregate("TrentBest/SimpleDemos",
        "abcdef0123456789abcdef0123456789abcdef01",
        new[] { ReleaseEvidenceCategory.BuildAndTests, ReleaseEvidenceCategory.PublicationGate,
            ReleaseEvidenceCategory.Security },
        new[] {
            NewReleaseEvidence(),
            NewReleaseEvidence(category: ReleaseEvidenceCategory.PublicationGate,
                state: ReleaseEvidenceState.Skipped, rawStatus: "completed", rawConclusion: "skipped")
        });
    var markdown = ReleaseEvidenceMarkdownRenderer.Render(report);
    Assert(!report.IsPassing, "incomplete report unexpectedly passed");
    Assert(markdown.Contains("Security | **Missing**", StringComparison.Ordinal),
        "missing gate was not visible");
    Assert(markdown.Contains("PublicationGate | **Skipped**", StringComparison.Ordinal),
        "skipped publication gate was not visible");
});
Check("Markdown release report escapes table delimiters in provider diagnostics", () =>
{
    var record = NewReleaseEvidence(diagnostic: "step | failed\nsecond line");
    var report = ReleaseEvidenceReport.Aggregate("TrentBest/SimpleDemos",
        "abcdef0123456789abcdef0123456789abcdef01",
        new[] { ReleaseEvidenceCategory.BuildAndTests }, new[] { record });
    var markdown = ReleaseEvidenceMarkdownRenderer.Render(report);
    Assert(markdown.Contains("step \\| failed second line", StringComparison.Ordinal),
        "diagnostic was able to break the Markdown table");
});

Console.WriteLine($"PASS: {passed} CiAi contract checks.");
await GitHubActionsRunClientTests.RunAsync();
await GitHubActionsWorkflowRunCollectorTests.RunAsync();
await GitHubActionsWorkflowJobCollectorTests.RunAsync();

void Check(string name, Action action)
{
    action();
    passed++;
    Console.WriteLine($"PASS {passed}: {name}");
}

async Task CheckAsync(string name, Func<Task> action)
{
    await action();
    passed++;
    Console.WriteLine($"PASS {passed}: {name}");
}

static async Task ThrowsAsync<TException>(Func<Task> action) where TException : Exception
{
    try { await action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}


static ReleaseEvidenceRecord NewReleaseEvidence(
    string repository = "TrentBest/SimpleDemos",
    string headSha = "abcdef0123456789abcdef0123456789abcdef01",
    ReleaseEvidenceCategory category = ReleaseEvidenceCategory.BuildAndTests,
    ReleaseEvidenceState state = ReleaseEvidenceState.Passed,
    string rawStatus = "completed",
    string rawConclusion = "success",
    bool directlyVerified = true,
    string? artifactDigestScope = "downloaded-nupkg-bytes",
    string? diagnostic = null) =>
    new(repository, headSha, category, state, rawStatus, rawConclusion,
        DateTimeOffset.Parse("2026-10-24T04:00:00Z"),
        new Uri("https://github.com/TrentBest/SimpleDemos/actions/runs/42"),
        runId: 42, runAttempt: 1, workflowIdentity: "contract-verification.yml",
        jobIdentity: "contract", diagnostic: diagnostic, artifactName: "candidate.nupkg",
        artifactDigest: new string('a', 64), artifactDigestScope: artifactDigestScope,
        directlyVerified: directlyVerified);

static CiRunSnapshot NewRun(
    string repository = "TrentBest/SimpleDemos",
    long runId = 42,
    CiRunStatus status = CiRunStatus.Completed,
    CiRunConclusion conclusion = CiRunConclusion.Success,
    Uri? url = null,
    string headSha = "abcdef0123456789abcdef0123456789abcdef01") =>
    new(repository, runId, "CiAi contract smoke test", headSha,
        status, conclusion, url ?? new Uri("https://github.com/TrentBest/SimpleDemos/actions/runs/42"),
        DateTimeOffset.Parse("2026-10-23T21:00:00Z"));

static ArtifactManifest NewManifest(
    string digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    Uri? uri = null,
    DateTimeOffset? expiresAt = null,
    string providerRepository = "TrentBest/SimpleDemos",
    long? providerRunId = 17,
    string sourceSha = "abcdef0123456789abcdef0123456789abcdef01") =>
    new("request-1", "operation-1", "idempotency-1", providerRepository,
        "uml.generate", "1.0.0", "abcdef0123456789abcdef0123456789abcdef01",
        "artifact-1", "v1", sourceSha,
        "SHA-256", digest, "application/json", 128,
        uri ?? new Uri("https://provider.example/artifacts/artifact-1"),
        DateTimeOffset.Parse("2026-10-24T00:00:00Z"),
        expiresAt ?? DateTimeOffset.Parse("2026-10-24T01:00:00Z"),
        "temporary", "client-ci", providerRunId);

static ArtifactManifest NewManifestForPayload(byte[] payload) =>
    new("request-1", "operation-1", "idempotency-1", "TrentBest/SimpleDemos",
        "uml.generate", "1.0.0", "abcdef0123456789abcdef0123456789abcdef01",
        "artifact-1", "v1", "abcdef0123456789abcdef0123456789abcdef01",
        "SHA-256", Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(), "application/octet-stream", payload.Length,
        new Uri("https://provider.example/artifacts/artifact-1"),
        DateTimeOffset.Parse("2026-10-24T00:00:00Z"),
        DateTimeOffset.Parse("2026-10-24T01:00:00Z"),
        "temporary", "client-ci", 17);

static ArtifactReceiptAcknowledgement NewAcknowledgement(
    bool retrieved = true,
    bool integrityVerified = true,
    ArtifactLocalValidationOutcome localValidation = ArtifactLocalValidationOutcome.NotRun,
    string acknowledgementId = "ack-1",
    string providerRepository = "TrentBest/SimpleDemos",
    long? providerRunId = 17,
    string sourceSha = "abcdef0123456789abcdef0123456789abcdef01") =>
    new(1, acknowledgementId, "request-1:artifact-1:v1", "request-1", "artifact-1", "v1",
        new string('a', 64), "client-ci", retrieved, integrityVerified,
        DateTimeOffset.Parse("2026-10-24T00:10:00Z"), localValidation,
        providerRepository: providerRepository, providerRunId: providerRunId,
        sourceSha: sourceSha);

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Throws<TException>(Action action) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
