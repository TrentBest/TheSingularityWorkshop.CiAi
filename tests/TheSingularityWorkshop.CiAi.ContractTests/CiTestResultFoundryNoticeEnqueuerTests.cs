using System.Security.Cryptography;
using TheSingularityWorkshop.CiAi;

internal static class CiTestResultFoundryNoticeEnqueuerTests
{
    public static async Task<int> RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "ciai-foundry-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var report = CiTestResultReportTests.CreateSampleReport();
            var bytes = report.ToCanonicalUtf8Bytes();
            var recipient = "foundry-host";
            var notifiedAt = report.ObservedAt.AddMinutes(2);
            var publisher = new FakePublisher((value, payload) =>
                CreateManifest(value, payload.Span, recipient, digestOverride: null, sourceShaOverride: null));
            var outbox = new FileFoundryArtifactReadyNoticeOutbox(Path.Combine(root, "notices.jsonl"));
            var enqueuer = new CiTestResultFoundryNoticeEnqueuer(publisher, outbox);

            var first = await enqueuer.PublishAndEnqueueAsync(
                report, recipient, "notice-report-42-1", notifiedAt);
            Assert(first == FoundryArtifactNoticeEnqueueOutcome.Enqueued,
                "canonical report was not durably enqueued for Foundry");
            Assert(publisher.PublishedBytes is not null && publisher.PublishedBytes.SequenceEqual(bytes),
                "artifact publisher did not receive the exact canonical report bytes");

            var pending = outbox.ReadPending();
            Assert(pending.Count == 1 &&
                pending[0].Notice.Manifest.Digest == report.ReportSha256 &&
                pending[0].Notice.Manifest.SizeBytes == bytes.Length &&
                pending[0].Notice.Manifest.IdempotencyKey == report.ReportIdentity &&
                pending[0].Notice.Manifest.SourceSha == report.CandidateSha &&
                pending[0].Notice.Manifest.ProviderRunId == report.RunId,
                "Foundry notice did not preserve report digest, size, identity, and exact run/candidate lineage");

            var duplicate = await enqueuer.PublishAndEnqueueAsync(
                report, recipient, "notice-report-42-1", notifiedAt);
            Assert(duplicate == FoundryArtifactNoticeEnqueueOutcome.Duplicate,
                "identical report notice retry was not idempotent");

            var badDigestOutbox = new FileFoundryArtifactReadyNoticeOutbox(Path.Combine(root, "bad-digest.jsonl"));
            var badDigestPublisher = new FakePublisher((value, payload) =>
                CreateManifest(value, payload.Span, recipient, new string('0', 64), null));
            var badDigestEnqueuer = new CiTestResultFoundryNoticeEnqueuer(badDigestPublisher, badDigestOutbox);
            await AssertThrowsAsync<InvalidDataException>(() => badDigestEnqueuer.PublishAndEnqueueAsync(
                report, recipient, "notice-bad-digest", notifiedAt));
            Assert(badDigestOutbox.ReadPending().Count == 0,
                "invalid publisher digest was persisted as a deliverable notice");

            var tamperedReportOutbox = new FileFoundryArtifactReadyNoticeOutbox(Path.Combine(root, "tampered-report.jsonl"));
            var tamperedReportPublisher = new FakePublisher((value, payload) =>
            {
                var alteredBytes = payload.ToArray();
                alteredBytes[0] ^= 0x01;
                return CreateManifest(value, alteredBytes, recipient, digestOverride: null, sourceShaOverride: null);
            });
            var tamperedReportEnqueuer = new CiTestResultFoundryNoticeEnqueuer(tamperedReportPublisher, tamperedReportOutbox);
            await AssertThrowsAsync<InvalidDataException>(() => tamperedReportEnqueuer.PublishAndEnqueueAsync(
                report, recipient, "notice-tampered-report", notifiedAt));
            Assert(tamperedReportOutbox.ReadPending().Count == 0,
                "publisher-substituted report bytes were persisted as a deliverable Foundry notice");

            var wrongCandidateOutbox = new FileFoundryArtifactReadyNoticeOutbox(Path.Combine(root, "wrong-candidate.jsonl"));
            var wrongCandidatePublisher = new FakePublisher((value, payload) =>
                CreateManifest(value, payload.Span, recipient, null, new string('f', 40)));
            var wrongCandidateEnqueuer = new CiTestResultFoundryNoticeEnqueuer(wrongCandidatePublisher, wrongCandidateOutbox);
            await AssertThrowsAsync<UnauthorizedAccessException>(() => wrongCandidateEnqueuer.PublishAndEnqueueAsync(
                report, recipient, "notice-wrong-candidate", notifiedAt));
            Assert(wrongCandidateOutbox.ReadPending().Count == 0,
                "wrong candidate SHA was persisted as a deliverable notice");

            Console.WriteLine("PASS: 7 canonical report-to-Foundry notice binding checks.");
            return 7;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ArtifactManifest CreateManifest(
        CiTestResultReport report,
        ReadOnlySpan<byte> bytes,
        string recipient,
        string? digestOverride,
        string? sourceShaOverride)
    {
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new ArtifactManifest(
            "request-" + report.RunId,
            "operation-" + report.RunId,
            report.ReportIdentity,
            report.Repository,
            CiTestResultFoundryNoticeEnqueuer.CapabilityId,
            CiTestResultFoundryNoticeEnqueuer.CapabilityVersion,
            report.WorkflowDefinitionSha,
            $"ciai-test-results-{report.RunId}-{report.RunAttempt}",
            "1",
            sourceShaOverride ?? report.CandidateSha,
            "SHA-256",
            digestOverride ?? digest,
            CiTestResultFoundryNoticeEnqueuer.MediaType,
            bytes.Length,
            new Uri("https://artifacts.example.com/ciai/test-results/report.json"),
            report.ObservedAt.AddSeconds(1),
            report.ObservedAt.AddHours(24),
            "ci-diagnostics",
            recipient,
            report.RunId);
    }

    private sealed class FakePublisher(
        Func<CiTestResultReport, ReadOnlyMemory<byte>, ArtifactManifest> createManifest)
        : ICiTestResultArtifactPublisher
    {
        public byte[]? PublishedBytes { get; private set; }

        public Task<ArtifactManifest> PublishAsync(
            CiTestResultReport report,
            ReadOnlyMemory<byte> canonicalReportBytes,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PublishedBytes = canonicalReportBytes.ToArray();
            return Task.FromResult(createManifest(report, canonicalReportBytes));
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try { await action().ConfigureAwait(false); }
        catch (TException) { return; }
        throw new InvalidOperationException("Expected " + typeof(TException).Name + ".");
    }
}
