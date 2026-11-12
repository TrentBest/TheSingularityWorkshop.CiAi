using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheSingularityWorkshop.CiAi;

internal static class CiWorkflowEvidenceDeliveryTests
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ciai-v1-delivery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var packet = CreatePacket();
            var delivery = new CiWorkflowEvidenceDelivery("v1-delivery-1", packet);
            Assert(delivery.IdempotencyKey.Length == 71 &&
                delivery.IdempotencyKey.StartsWith("sha256:", StringComparison.Ordinal) &&
                delivery.IdempotencyKey.AsSpan(7).ToString().All(Uri.IsHexDigit),
                "v1 idempotency identity was not encoded as a bounded SHA-256 key");
            Assert(new CiWorkflowEvidenceDelivery("v1-delivery-stable", packet).IdempotencyKey ==
                delivery.IdempotencyKey, "identical v1 provenance produced an unstable idempotency key");
            var outboxPath = Path.Combine(root, "v1-outbox.jsonl");
            var outbox = new FileCiWorkflowEvidenceOutbox(outboxPath);
            Assert(outbox.Enqueue(delivery) == CiWorkflowEvidenceEnqueueOutcome.Enqueued,
                "first v1 enqueue was not persisted");
            Assert(outbox.Enqueue(delivery) == CiWorkflowEvidenceEnqueueOutcome.Duplicate,
                "identical v1 delivery retry was not deduplicated");
            Assert(outbox.Enqueue(new CiWorkflowEvidenceDelivery("v1-delivery-alias", packet)) ==
                CiWorkflowEvidenceEnqueueOutcome.Duplicate,
                "v1 run attempt was not deduplicated across delivery IDs");

            var replay = new FileCiWorkflowEvidenceOutbox(Path.Combine(root, "v1-outbox.jsonl")).ReadPending();
            Assert(replay.Count == 1 && replay[0].PayloadSha256 == delivery.PayloadSha256 &&
                replay[0].IdempotencyKey == delivery.IdempotencyKey,
                "v1 outbox did not replay the exact payload and provenance identity");
            Assert(!replay[0].Packet.WorkflowDefinitionTrusted && !replay[0].Packet.IsSuccessful,
                "v1 durable reload improperly restored host workflow trust");

            var persistedOutbox = File.ReadAllText(outboxPath);
            var uppercaseKeyPath = Path.Combine(root, "v1-outbox-uppercase.jsonl");
            File.WriteAllText(uppercaseKeyPath, persistedOutbox.Replace(
                delivery.IdempotencyKey, "sha256:" + new string('A', 64), StringComparison.Ordinal));
            AssertThrows<InvalidDataException>(() =>
                new FileCiWorkflowEvidenceOutbox(uppercaseKeyPath).ReadPending());
            var nullKeyPath = Path.Combine(root, "v1-outbox-null-key.jsonl");
            File.WriteAllText(nullKeyPath, persistedOutbox.Replace(
                "\"IdempotencyKey\":\"" + delivery.IdempotencyKey + "\"",
                "\"IdempotencyKey\":null", StringComparison.Ordinal));
            AssertThrows<InvalidDataException>(() =>
                new FileCiWorkflowEvidenceOutbox(nullKeyPath).ReadPending());

            var changedPacket = CreatePacket(workflowName: "Different observed workflow label");
            var conflict = new CiWorkflowEvidenceDelivery("v1-delivery-conflict", changedPacket);
            Assert(outbox.Enqueue(conflict) == CiWorkflowEvidenceEnqueueOutcome.Conflict,
                "changed payload for the same workflow/checkout attempt did not conflict");

            var ledgerPath = Path.Combine(root, "v1-receipts.jsonl");
            var ledger = new FileCiWorkflowEvidenceReceiptLedger(ledgerPath);
            var accepted = new CiWorkflowEvidenceRemoteReceipt(
                1, "remote-receipt-1", delivery.DeliveryId, delivery.IdempotencyKey,
                delivery.PayloadSha256, CiWorkflowEvidenceReceiptOutcome.Accepted,
                DateTimeOffset.Parse("2026-10-31T02:00:00Z"));
            Assert(ledger.Record(accepted, delivery) == CiWorkflowEvidenceReceiptRecordOutcome.Recorded,
                "matching v1 acceptance receipt was not recorded");
            Assert(ledger.Record(accepted, delivery) == CiWorkflowEvidenceReceiptRecordOutcome.Duplicate,
                "identical v1 receipt retry was not idempotent");
            Assert(ledger.ReadAcknowledgedDeliveryIds().Contains(delivery.DeliveryId),
                "accepted v1 receipt did not acknowledge the exact delivery");
            Assert(new FileCiWorkflowEvidenceOutbox(Path.Combine(root, "v1-outbox.jsonl"))
                .ReadPending(ledger.ReadAcknowledgedDeliveryIds()).Count == 0,
                "accepted v1 receipt did not suppress replay");

            var rejectedDelivery = new CiWorkflowEvidenceDelivery("v1-delivery-rejected",
                CreatePacket(runId: 43));
            var rejected = new CiWorkflowEvidenceRemoteReceipt(
                1, "remote-receipt-2", rejectedDelivery.DeliveryId, rejectedDelivery.IdempotencyKey,
                rejectedDelivery.PayloadSha256, CiWorkflowEvidenceReceiptOutcome.Rejected,
                DateTimeOffset.Parse("2026-10-31T02:01:00Z"), "policy.incomplete");
            Assert(ledger.Record(rejected, rejectedDelivery) == CiWorkflowEvidenceReceiptRecordOutcome.Recorded,
                "v1 rejection receipt was not durably recorded");
            Assert(!ledger.ReadAcknowledgedDeliveryIds().Contains(rejectedDelivery.DeliveryId),
                "v1 rejection incorrectly acknowledged and suppressed replay");

            // Reload must fail closed if a damaged journal contains two distinct terminal
            // acceptances for the same delivery, even if each individual receipt is valid.
            var conflictingLedgerPath = Path.Combine(root, "v1-receipts-conflicting.jsonl");
            File.Copy(ledgerPath, conflictingLedgerPath);
            var conflictingReceiptJson = JsonSerializer.Serialize(new
            {
                contractVersion = 1,
                remoteReceiptId = "remote-receipt-forged-second-acceptance",
                deliveryId = delivery.DeliveryId,
                idempotencyKey = delivery.IdempotencyKey,
                payloadSha256 = delivery.PayloadSha256,
                outcome = "accepted",
                receivedAt = DateTimeOffset.Parse("2026-10-31T02:05:00Z")
            });
            var conflictingRecord = JsonSerializer.Serialize(new
            {
                RemoteReceiptId = "remote-receipt-forged-second-acceptance",
                DeliveryId = delivery.DeliveryId,
                Outcome = CiWorkflowEvidenceReceiptOutcome.Accepted,
                ReceiptJson = conflictingReceiptJson
            });
            File.AppendAllText(conflictingLedgerPath, conflictingRecord + "\n");
            AssertThrows<InvalidDataException>(() =>
                new FileCiWorkflowEvidenceReceiptLedger(conflictingLedgerPath).ReadAcknowledgedDeliveryIds());
            AssertThrows<ArgumentException>(() => ledger.Record(
                new CiWorkflowEvidenceRemoteReceipt(
                    1, "remote-receipt-3", delivery.DeliveryId, delivery.IdempotencyKey,
                    new string('0', 64), CiWorkflowEvidenceReceiptOutcome.Accepted,
                    DateTimeOffset.Parse("2026-10-31T02:02:00Z")), delivery));
            AssertThrows<ArgumentException>(() => new CiWorkflowEvidenceRemoteReceipt(
                1, "remote-receipt-invalid-key", delivery.DeliveryId, "not-a-v1-key",
                delivery.PayloadSha256, CiWorkflowEvidenceReceiptOutcome.Accepted,
                DateTimeOffset.Parse("2026-10-31T02:03:00Z")));
            AssertThrows<ArgumentException>(() => new CiWorkflowEvidenceRemoteReceipt(
                1, "remote-receipt-uppercase-key", delivery.DeliveryId,
                "sha256:" + new string('A', 64), delivery.PayloadSha256,
                CiWorkflowEvidenceReceiptOutcome.Accepted,
                DateTimeOffset.Parse("2026-10-31T02:04:00Z")));

            var receiptIdentityConflict = new CiWorkflowEvidenceRemoteReceipt(
                1, "remote-receipt-1", delivery.DeliveryId, delivery.IdempotencyKey,
                delivery.PayloadSha256, CiWorkflowEvidenceReceiptOutcome.Accepted,
                DateTimeOffset.Parse("2026-10-31T02:06:00Z"));
            Assert(ledger.Record(receiptIdentityConflict, delivery) == CiWorkflowEvidenceReceiptRecordOutcome.Conflict,
                "reused remote receipt identity with changed receipt bytes was not rejected as a conflict");
            Assert(ledger.ReadReceipts().Count == 2,
                "receipt ledger did not reload both accepted and rejected receipt records");

            var mismatch = new CiWorkflowEvidenceRemoteReceipt(
                1, "remote-receipt-mismatch", "different-delivery", delivery.IdempotencyKey,
                delivery.PayloadSha256, CiWorkflowEvidenceReceiptOutcome.Accepted,
                DateTimeOffset.Parse("2026-10-31T02:07:00Z"));
            Assert(mismatch.VerifyAgainst(delivery).Contains("deliveryId.mismatch"),
                "receipt verification did not detect a delivery identity mismatch");
            AssertThrows<ArgumentException>(() => ledger.Record(mismatch, delivery));

            var rejectionThenAcceptance = new CiWorkflowEvidenceDelivery("v1-delivery-rejection-retry",
                CreatePacket(runId: 44));
            var firstRejection = new CiWorkflowEvidenceRemoteReceipt(
                1, "remote-receipt-rejection-retry", rejectionThenAcceptance.DeliveryId,
                rejectionThenAcceptance.IdempotencyKey, rejectionThenAcceptance.PayloadSha256,
                CiWorkflowEvidenceReceiptOutcome.Rejected, DateTimeOffset.Parse("2026-10-31T02:08:00Z"),
                "policy.retryable");
            Assert(ledger.Record(firstRejection, rejectionThenAcceptance) == CiWorkflowEvidenceReceiptRecordOutcome.Recorded,
                "initial rejection was not recorded");
            Assert(ledger.Record(firstRejection, rejectionThenAcceptance) == CiWorkflowEvidenceReceiptRecordOutcome.Duplicate,
                "identical rejection retry was not idempotent");
            var laterAcceptance = new CiWorkflowEvidenceRemoteReceipt(
                1, "remote-receipt-later-acceptance", rejectionThenAcceptance.DeliveryId,
                rejectionThenAcceptance.IdempotencyKey, rejectionThenAcceptance.PayloadSha256,
                CiWorkflowEvidenceReceiptOutcome.Accepted, DateTimeOffset.Parse("2026-10-31T02:09:00Z"));
            Assert(ledger.Record(laterAcceptance, rejectionThenAcceptance) == CiWorkflowEvidenceReceiptRecordOutcome.Recorded,
                "a later valid acceptance was blocked by a prior rejection");
            Assert(ledger.ReadAcknowledgedDeliveryIds().Contains(rejectionThenAcceptance.DeliveryId),
                "later acceptance after rejection did not acknowledge the delivery");

            var tornLedgerPath = Path.Combine(root, "v1-receipts-torn.jsonl");
            File.WriteAllText(tornLedgerPath, File.ReadAllText(ledgerPath).TrimEnd() + "{");
            AssertThrows<InvalidDataException>(() =>
                new FileCiWorkflowEvidenceReceiptLedger(tornLedgerPath).ReadReceipts());
            var duplicateReceiptIdPath = Path.Combine(root, "v1-receipts-duplicate-id.jsonl");
            var validReceiptRecord = File.ReadLines(ledgerPath).First();
            File.WriteAllText(duplicateReceiptIdPath, validReceiptRecord + "\n" + validReceiptRecord + "\n");
            AssertThrows<InvalidDataException>(() =>
                new FileCiWorkflowEvidenceReceiptLedger(duplicateReceiptIdPath).ReadReceipts());

            AssertThrows<ArgumentException>(() => new CiWorkflowEvidenceRemoteReceipt(
                1, "remote-receipt-rejection-without-code", delivery.DeliveryId, delivery.IdempotencyKey,
                delivery.PayloadSha256, CiWorkflowEvidenceReceiptOutcome.Rejected,
                DateTimeOffset.Parse("2026-10-31T02:10:00Z")));
            AssertThrows<ArgumentException>(() => new CiWorkflowEvidenceRemoteReceipt(
                1, "remote-receipt-accepted-with-code", delivery.DeliveryId, delivery.IdempotencyKey,
                delivery.PayloadSha256, CiWorkflowEvidenceReceiptOutcome.Accepted,
                DateTimeOffset.Parse("2026-10-31T02:11:00Z"), "not.allowed"));

            Console.WriteLine("PASS: 30 CI workflow evidence v1 delivery and receipt checks.");
            return 30;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static CiWorkflowEvidencePacket CreatePacket(long runId = 42, string workflowName = "CI")
    {
        const string eventSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string workflowSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var json = """
        {
          "schema": "__SCHEMA__",
          "repository": "TrentBest/SimpleDemos",
          "runId": __RUN_ID__,
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
            .Replace("__RUN_ID__", runId.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
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
                new CiWorkflowStepObservation(1, "Build and test", "completed", "success", "2026-10-31T01:00:00Z")
            }));
        var observed = new CiWorkflowRunEvidenceObservation(
            "TrentBest/SimpleDemos", ".github/workflows/ci.yml", runId, 1,
            "refs/heads/development", eventSha, "push", workflowName,
            CiRunStatus.Completed, CiRunConclusion.Success,
            new Uri($"https://github.com/TrentBest/SimpleDemos/actions/runs/{runId}"),
            DateTimeOffset.Parse("2026-10-31T01:01:00Z"),
            Array.AsReadOnly(new[] { job }), true, true);
        return CiWorkflowEvidencePacket.FromValidatedProvenance(
            observed, provenance, workflowDefinitionTrusted: true);
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
