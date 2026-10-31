using System.Security.Cryptography;
using System.Text;
using TheSingularityWorkshop.CiAi;

internal static class CiWorkflowEvidenceDeliveryV3Tests
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ciai-v3-delivery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var packet = CreatePacket();
            var delivery = new CiWorkflowEvidenceDeliveryV3("v3-delivery-1", packet);
            var outbox = new FileCiWorkflowEvidenceOutboxV3(Path.Combine(root, "v3-outbox.jsonl"));
            Assert(outbox.Enqueue(delivery) == CiWorkflowEvidenceEnqueueV3Outcome.Enqueued,
                "first v3 enqueue was not persisted");
            Assert(outbox.Enqueue(delivery) == CiWorkflowEvidenceEnqueueV3Outcome.Duplicate,
                "identical v3 delivery retry was not deduplicated");
            Assert(outbox.Enqueue(new CiWorkflowEvidenceDeliveryV3("v3-delivery-alias", packet)) ==
                CiWorkflowEvidenceEnqueueV3Outcome.Duplicate,
                "v3 run attempt was not deduplicated across delivery IDs");

            var replay = new FileCiWorkflowEvidenceOutboxV3(Path.Combine(root, "v3-outbox.jsonl")).ReadPending();
            Assert(replay.Count == 1 && replay[0].PayloadSha256 == delivery.PayloadSha256 &&
                replay[0].IdempotencyKey == delivery.IdempotencyKey,
                "v3 outbox did not replay the exact payload and provenance identity");
            Assert(!replay[0].Packet.WorkflowDefinitionTrusted && !replay[0].Packet.IsSuccessful,
                "v3 durable reload improperly restored host workflow trust");

            var changedPacket = CreatePacket(workflowName: "Different observed workflow label");
            var conflict = new CiWorkflowEvidenceDeliveryV3("v3-delivery-conflict", changedPacket);
            Assert(outbox.Enqueue(conflict) == CiWorkflowEvidenceEnqueueV3Outcome.Conflict,
                "changed payload for the same workflow/checkout attempt did not conflict");

            var ledgerPath = Path.Combine(root, "v3-receipts.jsonl");
            var ledger = new FileCiWorkflowEvidenceReceiptLedgerV3(ledgerPath);
            var accepted = new CiWorkflowEvidenceRemoteReceiptV3(
                1, "remote-receipt-1", delivery.DeliveryId, delivery.IdempotencyKey,
                delivery.PayloadSha256, CiWorkflowEvidenceReceiptV3Outcome.Accepted,
                DateTimeOffset.Parse("2026-10-31T02:00:00Z"));
            Assert(ledger.Record(accepted, delivery) == CiWorkflowEvidenceReceiptV3RecordOutcome.Recorded,
                "matching v3 acceptance receipt was not recorded");
            Assert(ledger.Record(accepted, delivery) == CiWorkflowEvidenceReceiptV3RecordOutcome.Duplicate,
                "identical v3 receipt retry was not idempotent");
            Assert(ledger.ReadAcknowledgedDeliveryIds().Contains(delivery.DeliveryId),
                "accepted v3 receipt did not acknowledge the exact delivery");
            Assert(new FileCiWorkflowEvidenceOutboxV3(Path.Combine(root, "v3-outbox.jsonl"))
                .ReadPending(ledger.ReadAcknowledgedDeliveryIds()).Count == 0,
                "accepted v3 receipt did not suppress replay");

            var rejectedDelivery = new CiWorkflowEvidenceDeliveryV3("v3-delivery-rejected",
                CreatePacket(runId: 43));
            var rejected = new CiWorkflowEvidenceRemoteReceiptV3(
                1, "remote-receipt-2", rejectedDelivery.DeliveryId, rejectedDelivery.IdempotencyKey,
                rejectedDelivery.PayloadSha256, CiWorkflowEvidenceReceiptV3Outcome.Rejected,
                DateTimeOffset.Parse("2026-10-31T02:01:00Z"), "policy.incomplete");
            Assert(ledger.Record(rejected, rejectedDelivery) == CiWorkflowEvidenceReceiptV3RecordOutcome.Recorded,
                "v3 rejection receipt was not durably recorded");
            Assert(!ledger.ReadAcknowledgedDeliveryIds().Contains(rejectedDelivery.DeliveryId),
                "v3 rejection incorrectly acknowledged and suppressed replay");
            AssertThrows<ArgumentException>(() => ledger.Record(
                new CiWorkflowEvidenceRemoteReceiptV3(
                    1, "remote-receipt-3", delivery.DeliveryId, delivery.IdempotencyKey,
                    new string('0', 64), CiWorkflowEvidenceReceiptV3Outcome.Accepted,
                    DateTimeOffset.Parse("2026-10-31T02:02:00Z")), delivery));

            Console.WriteLine("PASS: 13 CI workflow evidence v3 delivery and receipt checks.");
            return 13;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static CiWorkflowEvidencePacketV3 CreatePacket(long runId = 42, string workflowName = "CI")
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
        var observed = new CiWorkflowEvidencePacket(
            "TrentBest/SimpleDemos", ".github/workflows/ci.yml", runId, 1,
            "refs/heads/development", eventSha, "push", workflowName,
            CiRunStatus.Completed, CiRunConclusion.Success,
            new Uri($"https://github.com/TrentBest/SimpleDemos/actions/runs/{runId}"),
            DateTimeOffset.Parse("2026-10-31T01:01:00Z"),
            Array.AsReadOnly(new[] { job }), true, true);
        return CiWorkflowEvidencePacketV3.FromValidatedProvenance(
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
