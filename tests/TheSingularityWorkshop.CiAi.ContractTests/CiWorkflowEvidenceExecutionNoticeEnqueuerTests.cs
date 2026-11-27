using System.Security.Cryptography;
using System.Text;
using TheSingularityWorkshop.CiAi;

internal static class CiWorkflowEvidenceExecutionNoticeEnqueuerTests
{
    private static readonly DateTimeOffset ObservedAt = DateTimeOffset.Parse("2026-11-27T20:00:00Z");
    private const string Recipient = "fsm-execution-host";

    public static async Task<int> RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "ciai-evidence-execution-notice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var packet = CreatePacket();
            var delivery = new CiWorkflowEvidenceDelivery(
                new CiWorkflowEvidenceDeliveryIdentity("evidence-request-42-1", "evidence-operation-42-1", "evidence-idempotency-42-1"),
                packet);
            var publisher = new FakePublisher((item, bytes) => CreateManifest(item, bytes.Span, Recipient));
            var outbox = new FileExecutionArtifactReadyNoticeOutbox(Path.Combine(root, "notices.jsonl"));
            var enqueuer = new CiWorkflowEvidenceExecutionNoticeEnqueuer(publisher, outbox);
            var notifiedAt = ObservedAt.AddMinutes(2);

            var outcome = await enqueuer.PublishAndEnqueueAsync(
                delivery, Recipient, "evidence-notice-42-1", notifiedAt);
            Assert(outcome == ExecutionArtifactNoticeEnqueueOutcome.Enqueued,
                "canonical V1 evidence did not enqueue an Execution ready notice");
            Assert(publisher.PublishedBytes is not null &&
                publisher.PublishedBytes.SequenceEqual(Encoding.UTF8.GetBytes(delivery.Payload)),
                "publisher did not receive the exact canonical V1 packet bytes");

            var pending = outbox.ReadPending();
            Assert(pending.Count == 1, "evidence notice was not durably persisted");
            var notice = pending[0].Notice;
            Assert(notice.Manifest.CapabilityId == CiWorkflowEvidenceExecutionNoticeEnqueuer.CapabilityId &&
                notice.Manifest.CapabilityVersion == "1" &&
                notice.Manifest.IdempotencyKey == delivery.IdempotencyKey &&
                notice.Manifest.Digest == delivery.PayloadSha256,
                "notice did not preserve the evidence capability, idempotency key, and exact packet digest");
            Assert(notice.Manifest.SourceSha == packet.CandidateSha &&
                notice.Manifest.ProviderRunId == packet.ObservedRun.RunId &&
                notice.Manifest.ImplementationRevision == packet.Provenance.WorkflowSha,
                "notice manifest lost candidate, run, or workflow-definition provenance");

            var duplicate = await enqueuer.PublishAndEnqueueAsync(
                delivery, Recipient, "evidence-notice-42-1", notifiedAt);
            Assert(duplicate == ExecutionArtifactNoticeEnqueueOutcome.Duplicate,
                "replaying the same evidence delivery did not deduplicate at the notice outbox");

            var evidenceOutbox = new FileCiWorkflowEvidenceOutbox(Path.Combine(root, "evidence.jsonl"));
            Assert(evidenceOutbox.Enqueue(delivery) == CiWorkflowEvidenceEnqueueOutcome.Enqueued,
                "source evidence packet was not durably queued");
            var batchNoticeOutbox = new FileExecutionArtifactReadyNoticeOutbox(Path.Combine(root, "batch-notices.jsonl"));
            var batchEnqueuer = new CiWorkflowEvidenceExecutionNoticeEnqueuer(
                new FakePublisher((item, bytes) => CreateManifest(item, bytes.Span, Recipient)), batchNoticeOutbox);
            var batch = await batchEnqueuer.PublishPendingAndEnqueueAsync(
                evidenceOutbox,
                new[] { new CiWorkflowRequiredCheckBinding("build", "build") },
                null,
                Recipient,
                notifiedAt);
            Assert(batch.PendingAtStart == 1 && batch.Attempted == 1 && batch.Enqueued == 1 &&
                batch.Duplicates == 0 && batch.Conflicts == 0,
                "durable evidence outbox was not projected into one Execution notice");
            Assert(batchNoticeOutbox.ReadPending().Count == 1,
                "outbox projection did not persist the resulting metadata-only notice");

            var wrongDigestPublisher = new FakePublisher((item, bytes) =>
                CreateManifest(item, bytes.Span, Recipient, digestOverride: new string('f', 64)));
            var wrongDigestEnqueuer = new CiWorkflowEvidenceExecutionNoticeEnqueuer(
                wrongDigestPublisher, new FileExecutionArtifactReadyNoticeOutbox(Path.Combine(root, "bad-digest.jsonl")));
            await AssertThrowsAsync<InvalidDataException>(() => wrongDigestEnqueuer.PublishAndEnqueueAsync(
                delivery, Recipient, "bad-digest-notice", notifiedAt));

            var wrongSourcePublisher = new FakePublisher((item, bytes) =>
                CreateManifest(item, bytes.Span, Recipient, sourceShaOverride: new string('c', 40)));
            var wrongSourceEnqueuer = new CiWorkflowEvidenceExecutionNoticeEnqueuer(
                wrongSourcePublisher, new FileExecutionArtifactReadyNoticeOutbox(Path.Combine(root, "bad-source.jsonl")));
            await AssertThrowsAsync<UnauthorizedAccessException>(() => wrongSourceEnqueuer.PublishAndEnqueueAsync(
                delivery, Recipient, "bad-source-notice", notifiedAt));

            Assert(outbox.ReadPending().Count == 1,
                "rejected manifests created additional notices");
            Console.WriteLine("PASS: 12 canonical V1 evidence-to-Execution artifact notice checks.");
            return 12;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ArtifactManifest CreateManifest(
        CiWorkflowEvidenceDelivery delivery,
        ReadOnlySpan<byte> bytes,
        string recipient,
        string? digestOverride = null,
        string? sourceShaOverride = null)
    {
        var packet = delivery.Packet;
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new ArtifactManifest(
            delivery.DeliveryId,
            delivery.OperationId,
            delivery.IdempotencyKey,
            packet.ObservedRun.Repository,
            CiWorkflowEvidenceExecutionNoticeEnqueuer.CapabilityId,
            CiWorkflowEvidenceExecutionNoticeEnqueuer.CapabilityVersion,
            packet.Provenance.WorkflowSha,
            $"ciai-workflow-evidence-{packet.ObservedRun.RunId}-{packet.ObservedRun.RunAttempt}",
            "1",
            sourceShaOverride ?? packet.CandidateSha,
            "SHA-256",
            digestOverride ?? digest,
            CiWorkflowEvidenceExecutionNoticeEnqueuer.MediaType,
            bytes.Length,
            new Uri("https://artifacts.example.com/ciai/workflow-evidence/packet.json"),
            ObservedAt.AddSeconds(-30),
            ObservedAt.AddHours(24),
            "ci-evidence",
            recipient,
            packet.ObservedRun.RunId);
    }

    private static CiWorkflowEvidencePacket CreatePacket()
    {
        const long runId = 42;
        const string eventSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string workflowSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var json = """
        {
          "schema": "__SCHEMA__",
          "repository": "TrentBest/SimpleDemos",
          "runId": 42,
          "runAttempt": 1,
          "workflow": {
            "ref": "TrentBest/SimpleDemos/.github/workflows/ci.yml@refs/heads/development",
            "sha": "__WORKFLOW_SHA__"
          },
          "event": {
            "name": "push",
            "ref": "refs/heads/development",
            "sha": "__EVENT_SHA__",
            "pullRequest": null
          },
          "checkout": {
            "ref": "refs/heads/development",
            "sha": "__EVENT_SHA__"
          }
        }
        """;
        json = json.Replace("__SCHEMA__", CiWorkflowProvenanceRecord.CurrentSchema, StringComparison.Ordinal)
            .Replace("__WORKFLOW_SHA__", workflowSha, StringComparison.Ordinal)
            .Replace("__EVENT_SHA__", eventSha, StringComparison.Ordinal);
        var bytes = Encoding.UTF8.GetBytes(json);
        var provenance = CiWorkflowProvenanceRecord.ParseAndValidate(
            bytes, "TrentBest/SimpleDemos", runId, 1,
            Convert.ToHexString(SHA256.HashData(bytes)));
        var job = new CiWorkflowJobObservation(100, runId, 1, "build", "completed", "success",
            new Uri($"https://github.com/TrentBest/SimpleDemos/actions/runs/{runId}/job/100"),
            Array.AsReadOnly(new[]
            {
                new CiWorkflowStepObservation(1, "Build and test", "completed", "success", ObservedAt.ToString("O"))
            }))
        {
            StartedAt = ObservedAt.AddMinutes(-2),
            CompletedAt = ObservedAt.AddMinutes(-1)
        };
        var observed = new CiWorkflowRunEvidenceObservation(
            "TrentBest/SimpleDemos", ".github/workflows/ci.yml", runId, 1,
            "refs/heads/development", eventSha, "push", "CI",
            CiRunStatus.Completed, CiRunConclusion.Success,
            new Uri($"https://github.com/TrentBest/SimpleDemos/actions/runs/{runId}"),
            ObservedAt,
            Array.AsReadOnly(new[] { job }), true, true);
        return CiWorkflowEvidencePacket.FromValidatedProvenance(
            observed, provenance, workflowDefinitionTrusted: true,
            new[] { new CiWorkflowRequiredCheckBinding("build", "build") });
    }

    private sealed class FakePublisher(
        Func<CiWorkflowEvidenceDelivery, ReadOnlyMemory<byte>, ArtifactManifest> createManifest)
        : ICiWorkflowEvidenceArtifactPublisher
    {
        public byte[]? PublishedBytes { get; private set; }

        public Task<ArtifactManifest> PublishAsync(
            CiWorkflowEvidenceDelivery delivery,
            ReadOnlyMemory<byte> canonicalPacketBytes,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PublishedBytes = canonicalPacketBytes.ToArray();
            return Task.FromResult(createManifest(delivery, canonicalPacketBytes));
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
