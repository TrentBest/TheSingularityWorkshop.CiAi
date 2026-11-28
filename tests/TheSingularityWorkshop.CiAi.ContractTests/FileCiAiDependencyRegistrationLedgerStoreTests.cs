using System.Security.Cryptography;
using System.Text;
using TheSingularityWorkshop.CiAi;

internal static class FileCiAiDependencyRegistrationLedgerStoreTests
{
    private const string WorkflowSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ciai-dependency-ledger-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileCiAiDependencyRegistrationLedgerStore(root);
            var first = CreateSnapshot("0123456789abcdef0123456789abcdef01234567", "TrentBest/Provider");
            var verification = CreateVerification(first, 42);
            var applied = store.ApplyVerifiedSnapshot(verification);
            Assert(verification.SourceRevision == 42 && verification.SourceCommitSha == first.SourceCommitSha &&
                verification.EvidencePayloadSha256.Length == 64,
                "verified source evidence did not retain exact run, SHA, and packet digest");
            AssertThrows<InvalidDataException>(() => CreateVerification(first, 42,
                allowedSourceRefs: new[] { "refs/heads/master" }));
            AssertThrows<InvalidDataException>(() => CreateVerification(first, 42, trustedWorkflow: false));
            AssertThrows<InvalidDataException>(() => CreateVerification(first, 42,
                conclusion: CiRunConclusion.Failure));

            var delivery = CiAiDependencyRegistrationDelivery.FromVerification(verification,
                new[] { "TrentBest/Provider", "TrentBest/PriorDependent" });
            var outbox = new FileCiAiDependencyRegistrationOutbox(Path.Combine(root, "outbox", "deliveries.jsonl"));
            Assert(outbox.Enqueue(delivery) == CiAiDependencyRegistrationOutboxEnqueueOutcome.Enqueued,
                "first federated delivery was not enqueued");
            Assert(outbox.Enqueue(delivery) == CiAiDependencyRegistrationOutboxEnqueueOutcome.Duplicate,
                "identical federated delivery replay was not deduplicated");
            var reopenedOutbox = new FileCiAiDependencyRegistrationOutbox(Path.Combine(root, "outbox", "deliveries.jsonl"));
            var reloadedDelivery = reopenedOutbox.ReadAll().Single();
            Assert(reloadedDelivery.Payload == delivery.Payload &&
                reloadedDelivery.PayloadSha256 == delivery.PayloadSha256 &&
                reloadedDelivery.RecipientRepositories.Contains("TrentBest/PriorDependent"),
                "delivery restart replay lost payload integrity or a withdrawal recipient");
            var changedRecipients = CiAiDependencyRegistrationDelivery.FromVerification(verification,
                new[] { "TrentBest/Provider" });
            Assert(reopenedOutbox.Enqueue(changedRecipients) == CiAiDependencyRegistrationOutboxEnqueueOutcome.Conflict,
                "delivery identity was reused with a changed recipient set");
            AssertThrows<System.Text.Json.JsonException>(() =>
                CiAiDependencyRegistrationDelivery.FromJson(delivery.Payload.Replace(
                    "\"schema\":", "\"unexpected\": 1, \"schema\":", StringComparison.Ordinal)));
            var acceptedReceipt = CiAiDependencyRegistrationReceipt.FromApplyResult(delivery,
                "TrentBest/Provider", applied);
            Assert(acceptedReceipt.Status == CiAiDependencyRegistrationReceiptStatus.Accepted &&
                acceptedReceipt.SourceRevision == 42 && acceptedReceipt.SourceCommitSha == first.SourceCommitSha &&
                acceptedReceipt.DeliveryPayloadSha256 == delivery.PayloadSha256,
                "accepted receipt did not bind exact delivery identity and digest");
            Assert(CiAiDependencyRegistrationReceipt.FromJson(acceptedReceipt.Payload).Payload == acceptedReceipt.Payload,
                "receipt JSON round-trip changed its canonical payload");
            var receiptStorePath = Path.Combine(root, "receipts", "receipts.jsonl");
            var receiptStore = new FileCiAiDependencyRegistrationReceiptStore(receiptStorePath);
            Assert(receiptStore.Record(acceptedReceipt) == CiAiDependencyRegistrationReceiptStoreOutcome.Recorded,
                "first receiver receipt was not durably recorded");
            Assert(new FileCiAiDependencyRegistrationReceiptStore(receiptStorePath).Record(acceptedReceipt) ==
                CiAiDependencyRegistrationReceiptStoreOutcome.Duplicate,
                "identical receiver receipt retry was not deduplicated after reopening");
            var contradictoryReceipt = CiAiDependencyRegistrationReceipt.FromApplyResult(delivery,
                "TrentBest/Provider", new CiAiDependencyRegistrationApplyResult(
                    CiAiDependencyRegistrationApplyStatus.Stale, applied.Ledger));
            Assert(receiptStore.Record(contradictoryReceipt) == CiAiDependencyRegistrationReceiptStoreOutcome.Conflict,
                "contradictory receipt reused the same delivery/recipient identity");
            Assert(receiptStore.ReadAll().Single().Payload == acceptedReceipt.Payload,
                "receipt journal did not preserve the accepted receipt");
            AssertThrows<ArgumentException>(() => CiAiDependencyRegistrationReceipt.FromApplyResult(delivery,
                "TrentBest/NotARecipient", applied));
            AssertThrows<System.Text.Json.JsonException>(() =>
                CiAiDependencyRegistrationReceipt.FromJson(acceptedReceipt.Payload.Replace(
                    "\\"schema\\":", "\\"unexpected\\": 1, \\"schema\\":", StringComparison.Ordinal)));
            var receiverLedger = new FileCiAiDependencyRegistrationLedgerStore(Path.Combine(root, "receiver-ledger"));
            var receiverReceipts = new FileCiAiDependencyRegistrationReceiptStore(
                Path.Combine(root, "receiver-receipts", "receipts.jsonl"));
            var receiver = new CiAiDependencyRegistrationReceiver("TrentBest/Provider", receiverLedger,
                receiverReceipts, new FixedDeliveryVerifier(verification));
            var received = receiver.ReceiveAsync(delivery.Payload).AsTask().GetAwaiter().GetResult();
            Assert(received.ApplyStatus == CiAiDependencyRegistrationApplyStatus.Applied &&
                received.Receipt.Status == CiAiDependencyRegistrationReceiptStatus.Accepted,
                "receiver did not independently verify, apply, and acknowledge the delivery");
            var replayed = receiver.ReceiveAsync(delivery.Payload).AsTask().GetAwaiter().GetResult();
            Assert(replayed.ApplyStatus == CiAiDependencyRegistrationApplyStatus.Duplicate &&
                replayed.ReceiptStoreOutcome == CiAiDependencyRegistrationReceiptStoreOutcome.Duplicate,
                "receiver replay did not reconcile idempotently before acknowledging");
            var mismatchedReceiver = new CiAiDependencyRegistrationReceiver("TrentBest/NotARecipient",
                receiverLedger, receiverReceipts, new FixedDeliveryVerifier(verification));
            AssertThrows<InvalidDataException>(() =>
                mismatchedReceiver.ReceiveAsync(delivery.Payload).AsTask().GetAwaiter().GetResult());
            var mismatchedEvidence = new CiAiDependencyRegistrationReceiver("TrentBest/Provider",
                receiverLedger, receiverReceipts, new FixedDeliveryVerifier(CreateVerification(
                    CreateSnapshot("5123456789abcdef0123456789abcdef01234567", "TrentBest/Provider"), 44)));
            AssertThrows<InvalidDataException>(() =>
                mismatchedEvidence.ReceiveAsync(delivery.Payload).AsTask().GetAwaiter().GetResult());
            Assert(applied.Status == CiAiDependencyRegistrationApplyStatus.Applied, "initial snapshot was not applied");
            Assert(store.ApplyVerifiedSnapshot(CreateVerification(first, 42)).Status == CiAiDependencyRegistrationApplyStatus.Duplicate,
                "identical replay was not idempotent");

            var reopened = new FileCiAiDependencyRegistrationLedgerStore(root);
            var loaded = reopened.Load("TrentBest/Consumer");
            Assert(loaded is not null && loaded.SourceRevision == 42 &&
                loaded.Registrations.Single().TargetRepository == "TrentBest/Provider",
                "ledger did not survive store reconstruction");

            var stale = CreateSnapshot("1123456789abcdef0123456789abcdef01234567", "TrentBest/Stale");
            Assert(reopened.ApplyVerifiedSnapshot(CreateVerification(stale, 41)).Status == CiAiDependencyRegistrationApplyStatus.Stale,
                "older source run was not rejected");
            AssertThrows<ArgumentOutOfRangeException>(() =>
                CiAiDependencyRegistrationLedger.ApplyVerifiedSnapshot(null, stale, 0));

            var conflict = CreateSnapshot("2123456789abcdef0123456789abcdef01234567", "TrentBest/Conflict");
            Assert(reopened.ApplyVerifiedSnapshot(CreateVerification(conflict, 42)).Status == CiAiDependencyRegistrationApplyStatus.Conflict,
                "same-run changed snapshot was not rejected");

            var replacement = CreateSnapshot("3123456789abcdef0123456789abcdef01234567");
            Assert(reopened.ApplyVerifiedSnapshot(CreateVerification(replacement, 43)).Status == CiAiDependencyRegistrationApplyStatus.Applied,
                "newer complete empty snapshot was not applied");
            Assert(reopened.Load("TrentBest/Consumer")!.Registrations.Count == 0,
                "newer empty snapshot did not withdraw prior registrations");

            var other = CreateSnapshot("4123456789abcdef0123456789abcdef01234567", source: "TrentBest/Other");
            Assert(reopened.ApplyVerifiedSnapshot(CreateVerification(other, 7)).Status == CiAiDependencyRegistrationApplyStatus.Applied,
                "independent source ledger was not stored");
            Assert(reopened.ReadAll().Count == 2, "source ledgers were not enumerated independently");

            var corrupt = Directory.GetFiles(root, "*.json").OrderBy(path => path, StringComparer.Ordinal).First();
            var corruptJson = File.ReadAllText(corrupt);
            var alteredDigest = System.Text.RegularExpressions.Regex.Replace(corruptJson,
                "\"SnapshotSha256\": \"[0-9a-f]{64}\"",
                "\"SnapshotSha256\": \"" + new string('0', 64) + "\"");
            Assert(!string.Equals(corruptJson, alteredDigest, StringComparison.Ordinal),
                "corruption fixture did not locate the stored digest");
            File.WriteAllText(corrupt, alteredDigest);
            AssertThrows<InvalidDataException>(() => reopened.ReadAll());

            Console.WriteLine("PASS: 25 durable dependency federation, receiver, and receipt contract checks.");
            return 25;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static CiAiDependencyRegistrationSnapshot CreateSnapshot(
        string sha, string? target = null, string source = "TrentBest/Consumer")
    {
        var builder = CiAi.ForRepository(source);
        if (target is not null) builder = builder.WithDependency(target);
        return CiAiDependencyRegistrationSnapshot.FromDefinition(builder.Build(), sha);
    }

    private static CiAiDependencyRegistrationSourceVerification CreateVerification(
        CiAiDependencyRegistrationSnapshot snapshot, long runId,
        string[]? allowedSourceRefs = null, bool trustedWorkflow = true,
        CiRunConclusion conclusion = CiRunConclusion.Success)
    {
        var sourceRef = "refs/heads/development";
        var provenanceJson = $$"""
        {
          "schema": "{{CiWorkflowProvenanceRecord.CurrentSchema}}",
          "repository": "{{snapshot.SourceRepository}}",
          "runId": {{runId}},
          "runAttempt": 1,
          "workflow": {
            "ref": "{{snapshot.SourceRepository}}/.github/workflows/ci.yml@{{sourceRef}}",
            "sha": "{{WorkflowSha}}"
          },
          "event": {
            "name": "push",
            "ref": "{{sourceRef}}",
            "sha": "{{snapshot.SourceCommitSha}}",
            "pullRequest": null
          },
          "checkout": {
            "ref": "{{sourceRef}}",
            "sha": "{{snapshot.SourceCommitSha}}"
          }
        }
        """;
        var bytes = Encoding.UTF8.GetBytes(provenanceJson);
        var provenance = CiWorkflowProvenanceRecord.ParseAndValidate(bytes, snapshot.SourceRepository,
            runId, 1, Convert.ToHexString(SHA256.HashData(bytes)));
        var policy = new CiWorkflowDefinitionTrustPolicy(snapshot.SourceRepository,
            ".github/workflows/ci.yml", sourceRef, WorkflowSha);
        var workflowBytes = Encoding.UTF8.GetBytes("name: trusted source CI");
        var approvedWorkflowBytes = trustedWorkflow
            ? workflowBytes
            : Encoding.UTF8.GetBytes("different protected workflow content");
        var trustDecision = policy.Evaluate(provenance, workflowBytes, approvedWorkflowBytes);

        var job = new CiWorkflowJobObservation(100, runId, 1, "build-and-test", "completed", "success",
            new Uri($"https://github.com/{snapshot.SourceRepository}/actions/runs/{runId}/job/100"),
            Array.AsReadOnly(new[]
            {
                new CiWorkflowStepObservation(1, "Build and test", "completed", "success", "2026-11-28T00:00:00Z")
            }))
        {
            StartedAt = DateTimeOffset.Parse("2026-11-28T00:00:00Z"),
            CompletedAt = DateTimeOffset.Parse("2026-11-28T00:01:00Z")
        };
        var observedRun = new CiWorkflowRunEvidenceObservation(snapshot.SourceRepository,
            ".github/workflows/ci.yml", runId, 1, sourceRef, snapshot.SourceCommitSha,
            "push", "CI", CiRunStatus.Completed, conclusion,
            new Uri($"https://github.com/{snapshot.SourceRepository}/actions/runs/{runId}"),
            DateTimeOffset.Parse("2026-11-28T00:02:00Z"),
            Array.AsReadOnly(new[] { job }), true, true);
        var packet = CiWorkflowEvidencePacket.FromValidatedProvenance(observedRun, provenance, trustDecision);
        var packetV2 = CiWorkflowEvidencePacketV2.FromPacket(packet,
            new[] { new CiWorkflowRequiredCheckBinding("build-and-test", "build-and-test") });
        return CiAiDependencyRegistrationSourceVerification.Verify(snapshot, packetV2, trustDecision,
            allowedSourceRefs ?? new[] { sourceRef });
    }

    private sealed class FixedDeliveryVerifier(
        CiAiDependencyRegistrationSourceVerification verification)
        : ICiAiDependencyRegistrationDeliveryVerifier
    {
        public ValueTask<CiAiDependencyRegistrationSourceVerification> VerifyAsync(
            CiAiDependencyRegistrationDelivery delivery, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(verification);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException("Expected " + typeof(TException).Name + ".");
    }
}
