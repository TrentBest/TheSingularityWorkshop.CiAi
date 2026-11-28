using System.Security.Cryptography;
using TheSingularityWorkshop.CiAi;

internal static class CiTestResultExecutionNoticeEnqueuerTests
{
    public static async Task<int> RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "ciai-execution-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var report = CiTestResultReportTests.CreateSampleReport();
            var bytes = report.ToCanonicalUtf8Bytes();
            var recipient = "foundry-host";
            var identity = new CiAiArtifactDeliveryIdentity("request-42-1", "operation-42-1", "idempotency-42-1");
            var notifiedAt = report.ObservedAt.AddMinutes(2);
            var publisher = new FakePublisher((value, dispatch, payload) =>
                CreateManifest(value, dispatch, payload.Span, recipient, digestOverride: null, sourceShaOverride: null));
            var outbox = new FileExecutionArtifactReadyNoticeOutbox(Path.Combine(root, "notices.jsonl"));
            var enqueuer = new CiTestResultExecutionNoticeEnqueuer(publisher, outbox);

            var first = await enqueuer.PublishAndEnqueueAsync(
                report, identity, recipient, notifiedAt);
            Assert(first == ExecutionArtifactNoticeEnqueueOutcome.Enqueued,
                "canonical report was not durably enqueued for FSM_Execution");
            Assert(publisher.PublishedBytes is not null && publisher.PublishedBytes.SequenceEqual(bytes),
                "artifact publisher did not receive the exact canonical report bytes");

            var pending = outbox.ReadPending();
            Assert(pending.Count == 1 &&
                pending[0].Notice.NoticeId == CiTestResultExecutionNoticeEnqueuer.CreateStableNoticeId(identity) &&
                pending[0].Notice.Manifest.Digest == report.ReportSha256 &&
                pending[0].Notice.Manifest.SizeBytes == bytes.Length &&
                pending[0].Notice.Manifest.RequestId == identity.RequestId &&
                pending[0].Notice.Manifest.OperationId == identity.OperationId &&
                pending[0].Notice.Manifest.IdempotencyKey == identity.DeriveIdempotencyKey(CiTestResultExecutionNoticeEnqueuer.CapabilityId) &&
                pending[0].Notice.Manifest.SourceSha == report.CandidateSha &&
                pending[0].Notice.Manifest.ProviderRunId == report.RunId,
                "FSM_Execution notice did not preserve report digest, size, identity, and exact run/candidate lineage");

            var duplicate = await enqueuer.PublishAndEnqueueAsync(
                report, identity, recipient, "notice-report-42-1", notifiedAt);
            Assert(duplicate == ExecutionArtifactNoticeEnqueueOutcome.Duplicate,
                "identical report notice retry was not idempotent");

            var badDigestOutbox = new FileExecutionArtifactReadyNoticeOutbox(Path.Combine(root, "bad-digest.jsonl"));
            var badDigestPublisher = new FakePublisher((value, dispatch, payload) =>
                CreateManifest(value, identity, payload.Span, recipient, new string('0', 64), null));
            var badDigestEnqueuer = new CiTestResultExecutionNoticeEnqueuer(badDigestPublisher, badDigestOutbox);
            await AssertThrowsAsync<InvalidDataException>(() => badDigestEnqueuer.PublishAndEnqueueAsync(
                report, identity, recipient, "notice-bad-digest", notifiedAt));
            Assert(badDigestOutbox.ReadPending().Count == 0,
                "invalid publisher digest was persisted as a deliverable notice");

            var tamperedReportOutbox = new FileExecutionArtifactReadyNoticeOutbox(Path.Combine(root, "tampered-report.jsonl"));
            var tamperedReportPublisher = new FakePublisher((value, dispatch, payload) =>
            {
                var alteredBytes = payload.ToArray();
                alteredBytes[0] ^= 0x01;
                return CreateManifest(value, identity, alteredBytes, recipient, digestOverride: null, sourceShaOverride: null);
            });
            var tamperedReportEnqueuer = new CiTestResultExecutionNoticeEnqueuer(tamperedReportPublisher, tamperedReportOutbox);
            await AssertThrowsAsync<InvalidDataException>(() => tamperedReportEnqueuer.PublishAndEnqueueAsync(
                report, identity, recipient, "notice-tampered-report", notifiedAt));
            Assert(tamperedReportOutbox.ReadPending().Count == 0,
                "publisher-substituted report bytes were persisted as a deliverable FSM_Execution notice");

            var wrongCandidateOutbox = new FileExecutionArtifactReadyNoticeOutbox(Path.Combine(root, "wrong-candidate.jsonl"));
            var wrongCandidatePublisher = new FakePublisher((value, dispatch, payload) =>
                CreateManifest(value, identity, payload.Span, recipient, null, new string('f', 40)));
            var wrongCandidateEnqueuer = new CiTestResultExecutionNoticeEnqueuer(wrongCandidatePublisher, wrongCandidateOutbox);
            await AssertThrowsAsync<UnauthorizedAccessException>(() => wrongCandidateEnqueuer.PublishAndEnqueueAsync(
                report, identity, recipient, "notice-wrong-candidate", notifiedAt));
            Assert(wrongCandidateOutbox.ReadPending().Count == 0,
                "wrong candidate SHA was persisted as a deliverable notice");

            Console.WriteLine("PASS: 7 canonical report-to-FSM_Execution notice binding checks.");
            return 7;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ArtifactManifest CreateManifest(
        CiTestResultReport report,
        CiAiArtifactDeliveryIdentity identity,
        ReadOnlySpan<byte> bytes,
        string recipient,
        string? digestOverride,
        string? sourceShaOverride)
    {
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new ArtifactManifest(
            identity.RequestId,
            identity.OperationId,
            identity.DeriveIdempotencyKey(CiTestResultExecutionNoticeEnqueuer.CapabilityId),
            report.Repository,
            CiTestResultExecutionNoticeEnqueuer.CapabilityId,
            CiTestResultExecutionNoticeEnqueuer.CapabilityVersion,
            report.WorkflowDefinitionSha,
            $"ciai-test-results-{report.RunId}-{report.RunAttempt}",
            "1",
            sourceShaOverride ?? report.CandidateSha,
            "SHA-256",
            digestOverride ?? digest,
            CiTestResultExecutionNoticeEnqueuer.MediaType,
            bytes.Length,
            new Uri("https://artifacts.example.com/ciai/test-results/report.json"),
            report.ObservedAt.AddSeconds(1),
            report.ObservedAt.AddHours(24),
            "ci-diagnostics",
            recipient,
            report.RunId);
    }

    private sealed class FakePublisher(
        Func<CiTestResultReport, CiAiArtifactDeliveryIdentity, ReadOnlyMemory<byte>, ArtifactManifest> createManifest)
        : ICiTestResultArtifactPublisher
    {
        public byte[]? PublishedBytes { get; private set; }

        public Task<ArtifactManifest> PublishAsync(
            CiTestResultReport report,
            CiAiArtifactDeliveryIdentity identity,
            ReadOnlyMemory<byte> canonicalReportBytes,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PublishedBytes = canonicalReportBytes.ToArray();
            return Task.FromResult(createManifest(report, identity, canonicalReportBytes));
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
