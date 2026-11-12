using TheSingularityWorkshop.CiAi;

internal static class FoundryArtifactReadyNoticeOutboxTests
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ciai-foundry-notices-" + Guid.NewGuid().ToString("N"));
        var outboxPath = Path.Combine(root, "notices.jsonl");
        var receiptPath = Path.Combine(root, "receipts.jsonl");
        try
        {
            var notice = CreateNotice();
            var delivery = new FoundryArtifactReadyNoticeDelivery(notice);
            var outbox = new FileFoundryArtifactReadyNoticeOutbox(outboxPath);
            Assert(outbox.Enqueue(delivery) == FoundryArtifactNoticeEnqueueOutcome.Enqueued,
                "first notice was not durably enqueued");
            Assert(outbox.Enqueue(delivery) == FoundryArtifactNoticeEnqueueOutcome.Duplicate,
                "exact notice retry was not deduplicated");

            var reopened = new FileFoundryArtifactReadyNoticeOutbox(outboxPath);
            var pending = reopened.ReadPending();
            Assert(pending.Count == 1 && pending[0].Payload == delivery.Payload &&
                pending[0].PayloadSha256 == delivery.PayloadSha256,
                "restart replay did not preserve exact notice bytes and digest");

            var changedSameNotice = new FoundryArtifactReadyNoticeDelivery(
                CreateNotice(noticeId: notice.NoticeId, digest: new string('b', 64)));
            Assert(reopened.Enqueue(changedSameNotice) == FoundryArtifactNoticeEnqueueOutcome.Conflict,
                "changed payload reused an existing notice identity");
            var changedNoticeId = new FoundryArtifactReadyNoticeDelivery(
                CreateNotice(noticeId: "notice-2"));
            Assert(reopened.Enqueue(changedNoticeId) == FoundryArtifactNoticeEnqueueOutcome.Conflict,
                "idempotency key was reused under a different notice ID");

            var receipt = CreateReceipt(delivery, "receipt-1", "accepted");
            var ledger = new FileFoundryArtifactIngressReceiptLedger(receiptPath);
            Assert(ledger.Record(receipt, delivery) == FoundryArtifactIngressReceiptRecordOutcome.Recorded,
                "durable accepted receipt was not recorded");
            Assert(ledger.ReadAcknowledgedNoticeIds().Contains(notice.NoticeId),
                "accepted receipt did not acknowledge the exact notice");
            Assert(reopened.ReadPending(ledger.ReadAcknowledgedNoticeIds()).Count == 0,
                "accepted notice was replayed after restart");

            var duplicateReceipt = CreateReceipt(delivery, "receipt-1", "duplicate");
            Assert(ledger.Record(duplicateReceipt, delivery) == FoundryArtifactIngressReceiptRecordOutcome.Duplicate,
                "same stable receipt returned as duplicate was treated as conflict");
            Assert(ledger.ReadReceipts().Single().Outcome == "accepted",
                "duplicate retry overwrote the original durable receipt");

            var wrongReceipt = new FoundryArtifactIngressReceipt(
                FoundryArtifactReadyNoticeClient.Contract, "receipt-wrong", "other-notice",
                delivery.IdempotencyKey, "accepted", DateTimeOffset.Parse("2026-10-31T00:02:00Z"));
            AssertThrows<ArgumentException>(() => ledger.Record(wrongReceipt, delivery));

            var tornOutbox = Path.Combine(root, "torn-outbox.jsonl");
            File.WriteAllText(tornOutbox, "partial");
            AssertThrows<InvalidDataException>(() =>
                new FileFoundryArtifactReadyNoticeOutbox(tornOutbox).ReadPending());
            var tornLedger = Path.Combine(root, "torn-ledger.jsonl");
            File.WriteAllText(tornLedger, "partial");
            AssertThrows<InvalidDataException>(() =>
                new FileFoundryArtifactIngressReceiptLedger(tornLedger).ReadAcknowledgedNoticeIds());

            Console.WriteLine("PASS: 13 Foundry notice outbox and receipt ledger contract checks.");
            return 13;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ArtifactReadyNotice CreateNotice(string noticeId = "notice-1", string digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")
    {
        var manifest = new ArtifactManifest(
            "request-1", "operation-1", "TrentBest/SimpleDemos:42:1",
            "TrentBest/CiAi", "ci.evidence", "1.0.0",
            "abcdef0123456789abcdef0123456789abcdef01",
            "artifact-42-1", "v1", "abcdef0123456789abcdef0123456789abcdef01",
            "SHA-256", digest, "application/json", 1024,
            new Uri("https://artifacts.example/evidence/42/1"),
            DateTimeOffset.Parse("2026-10-31T00:00:00Z"),
            DateTimeOffset.Parse("2026-10-31T01:00:00Z"),
            "temporary", "foundry-myvr", 42);
        return new ArtifactReadyNotice(1, noticeId, "foundry-myvr", manifest,
            DateTimeOffset.Parse("2026-10-31T00:01:00Z"));
    }

    private static FoundryArtifactIngressReceipt CreateReceipt(
        FoundryArtifactReadyNoticeDelivery delivery, string receiptId, string outcome) =>
        new(FoundryArtifactReadyNoticeClient.Contract, receiptId, delivery.NoticeId,
            delivery.IdempotencyKey, outcome, DateTimeOffset.Parse("2026-10-31T00:02:00Z"));

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
