using TheSingularityWorkshop.CiAi;

internal static class CiWorkflowEvidenceReceiptLedgerContractTests
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ciai-evidence-receipts-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "receipts.jsonl");
        try
        {
            var delivery = CreateDelivery();
            var ledger = new FileCiWorkflowEvidenceReceiptLedger(path);
            var rejected = CreateReceipt(delivery, "receipt-rejected", CiWorkflowEvidenceReceiptOutcome.Rejected, "policy.denied");
            Assert(ledger.Record(rejected, delivery) == CiWorkflowEvidenceReceiptRecordOutcome.Recorded,
                "rejection receipt was not recorded");
            Assert(ledger.ReadAcknowledgedDeliveryIds().Count == 0,
                "policy rejection incorrectly acknowledged the delivery");
            Assert(ledger.ReadReceipts().Single().RejectionCode == "policy.denied",
                "rejection reason was not preserved");

            var accepted = CreateReceipt(delivery, "receipt-accepted", CiWorkflowEvidenceReceiptOutcome.Accepted);
            Assert(ledger.Record(accepted, delivery) == CiWorkflowEvidenceReceiptRecordOutcome.Recorded,
                "accepted receipt was not recorded");
            Assert(ledger.ReadAcknowledgedDeliveryIds().Contains(delivery.DeliveryId),
                "durable accepted receipt did not acknowledge the delivery");
            Assert(ledger.Record(accepted, delivery) == CiWorkflowEvidenceReceiptRecordOutcome.Duplicate,
                "exact receipt retry was not idempotent");

            var reopened = new FileCiWorkflowEvidenceReceiptLedger(path);
            Assert(reopened.ReadReceipts().Count == 2 &&
                reopened.ReadAcknowledgedDeliveryIds().Contains(delivery.DeliveryId),
                "receipt history or acknowledgement did not survive reopen");

            var wrongDigest = new CiWorkflowEvidenceRemoteReceipt(1, "receipt-wrong", delivery.DeliveryId,
                delivery.IdempotencyKey, new string('0', 64), CiWorkflowEvidenceReceiptOutcome.Accepted,
                DateTimeOffset.Parse("2026-10-30T23:00:00Z"));
            AssertThrows<ArgumentException>(() => reopened.Record(wrongDigest, delivery));

            var conflict = new CiWorkflowEvidenceRemoteReceipt(1, "receipt-accepted", delivery.DeliveryId,
                delivery.IdempotencyKey, delivery.PayloadSha256, CiWorkflowEvidenceReceiptOutcome.Duplicate,
                DateTimeOffset.Parse("2026-10-30T23:01:00Z"));
            Assert(reopened.Record(conflict, delivery) == CiWorkflowEvidenceReceiptRecordOutcome.Conflict,
                "remote receipt ID reuse with changed outcome did not conflict");

            var nextDelivery = CreateDelivery("delivery-2", 43);
            var nextReceipt = CreateReceipt(nextDelivery, "receipt-next", CiWorkflowEvidenceReceiptOutcome.Duplicate);
            Assert(reopened.Record(nextReceipt, nextDelivery) == CiWorkflowEvidenceReceiptRecordOutcome.Recorded,
                "duplicate receipt was not recorded");
            Assert(reopened.ReadAcknowledgedDeliveryIds().SetEquals(new[] { delivery.DeliveryId, nextDelivery.DeliveryId }),
                "accepted and duplicate receipts were not both acknowledged");

            var tornPath = Path.Combine(root, "torn.jsonl");
            File.WriteAllText(tornPath, "partial");
            AssertThrows<InvalidDataException>(() =>
                new FileCiWorkflowEvidenceReceiptLedger(tornPath).ReadAcknowledgedDeliveryIds());

            Console.WriteLine("PASS: 12 CI workflow evidence receipt ledger contract checks.");
            return 8;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static CiWorkflowEvidenceDelivery CreateDelivery(string id = "delivery-1", long runId = 42)
    {
        var job = new CiWorkflowJobObservation(101, runId, 1, "build", "completed", "success",
            new Uri($"https://github.com/TrentBest/SimpleDemos/actions/runs/{runId}/job/101"),
            Array.AsReadOnly(new[] { new CiWorkflowStepObservation(1, "Tests", "completed", "success", "2026-10-30T22:00:00Z") }));
        var packet = new CiWorkflowEvidencePacket("TrentBest/SimpleDemos", ".github/workflows/ci.yml",
            runId, 1, "refs/heads/feature/agent-fix", "abcdef0123456789abcdef0123456789abcdef01",
            "push", "CI", CiRunStatus.Completed, CiRunConclusion.Success,
            new Uri($"https://github.com/TrentBest/SimpleDemos/actions/runs/{runId}"),
            DateTimeOffset.Parse("2026-10-30T22:01:00Z"), Array.AsReadOnly(new[] { job }), true, true);
        return new CiWorkflowEvidenceDelivery(id, packet);
    }

    private static CiWorkflowEvidenceRemoteReceipt CreateReceipt(CiWorkflowEvidenceDelivery delivery,
        string receiptId, CiWorkflowEvidenceReceiptOutcome outcome, string? rejectionCode = null) =>
        new(1, receiptId, delivery.DeliveryId, delivery.IdempotencyKey, delivery.PayloadSha256,
            outcome, DateTimeOffset.Parse("2026-10-30T23:00:00Z"), rejectionCode);

    private static void AssertThrows<TException>(Action action) where TException : Exception
        { try { action(); } catch (TException) { return; } throw new InvalidOperationException("Expected " + typeof(TException).Name + "."); }
    private static void Assert(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
}
