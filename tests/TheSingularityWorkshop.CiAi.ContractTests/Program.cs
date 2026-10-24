using System.Security.Cryptography;
using TheSingularityWorkshop.CiAi;

var passed = 0;
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

Console.WriteLine($"PASS: {passed} CiAi contract checks.");
await GitHubActionsRunClientTests.RunAsync();

void Check(string name, Action action)
{
    action();
    passed++;
    Console.WriteLine($"PASS {passed}: {name}");
}

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
    DateTimeOffset? expiresAt = null) =>
    new("request-1", "operation-1", "idempotency-1", "TrentBest/SimpleDemos",
        "uml.generate", "1.0.0", "abcdef0123456789abcdef0123456789abcdef01",
        "artifact-1", "v1", "abcdef0123456789abcdef0123456789abcdef01",
        "SHA-256", digest, "application/json", 128,
        uri ?? new Uri("https://provider.example/artifacts/artifact-1"),
        DateTimeOffset.Parse("2026-10-24T00:00:00Z"),
        expiresAt ?? DateTimeOffset.Parse("2026-10-24T01:00:00Z"),
        "temporary", "client-ci", 17);

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
    ArtifactLocalValidationOutcome localValidation = ArtifactLocalValidationOutcome.NotRun) =>
    new(1, "ack-1", "request-1:artifact-1:v1", "request-1", "artifact-1", "v1",
        new string('a', 64), "client-ci", retrieved, integrityVerified,
        DateTimeOffset.Parse("2026-10-24T00:10:00Z"), localValidation);

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
