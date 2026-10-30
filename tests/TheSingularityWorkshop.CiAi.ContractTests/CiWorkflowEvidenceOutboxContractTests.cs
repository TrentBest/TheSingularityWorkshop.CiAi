using TheSingularityWorkshop.CiAi;

internal static class CiWorkflowEvidenceOutboxContractTests
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ciai-outbox-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "evidence.jsonl");
        try
        {
            var packet = CreatePacket();
            var delivery = new CiWorkflowEvidenceDelivery("delivery-1", packet);
            var outbox = new FileCiWorkflowEvidenceOutbox(path);
            Assert(outbox.Enqueue(delivery) == CiWorkflowEvidenceEnqueueOutcome.Enqueued,
                "first enqueue was not persisted");
            Assert(outbox.Enqueue(delivery) == CiWorkflowEvidenceEnqueueOutcome.Duplicate,
                "identical retry was not deduplicated");
            Assert(outbox.Enqueue(new CiWorkflowEvidenceDelivery("delivery-alias", packet)) ==
                CiWorkflowEvidenceEnqueueOutcome.Duplicate,
                "same workflow attempt under a second delivery ID was not deduplicated");
            Assert(outbox.ReadPending().Count == 1, "pending evidence was not replayable");
            var reopened = new FileCiWorkflowEvidenceOutbox(path);
            var replay = reopened.ReadPending();
            Assert(replay.Count == 1 && replay[0].Packet.IsSuccessful &&
                replay[0].IdempotencyKey == delivery.IdempotencyKey &&
                replay[0].PayloadSha256 == delivery.PayloadSha256,
                "reopened outbox did not preserve exact packet identity and bytes");
            Assert(reopened.ReadPending(new HashSet<string> { "delivery-1" }).Count == 0,
                "acknowledged delivery was not filtered by caller-owned receipt state");

            var changed = CreatePacket(runId: 43);
            Assert(reopened.Enqueue(new CiWorkflowEvidenceDelivery("delivery-1", changed)) ==
                CiWorkflowEvidenceEnqueueOutcome.Conflict,
                "delivery ID reuse with different evidence did not conflict");
            var sameAttemptDifferentEvidence = CreatePacket(workflowName: "Changed workflow metadata");
            Assert(reopened.Enqueue(new CiWorkflowEvidenceDelivery("delivery-conflict", sameAttemptDifferentEvidence)) ==
                CiWorkflowEvidenceEnqueueOutcome.Conflict,
                "same run-attempt key with changed evidence did not conflict");
            Assert(reopened.ReadPending().Count == 1,
                "conflicting enqueue changed the original durable evidence");

            Console.WriteLine("PASS: 11 CI workflow evidence outbox contract checks.");
            return 11;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static CiWorkflowEvidencePacket CreatePacket(long runId = 42, string workflowName = "CI")
    {
        var job = new CiWorkflowJobObservation(101, runId, 1, "build",
            "completed", "success",
            new Uri($"https://github.com/TrentBest/SimpleDemos/actions/runs/{runId}/job/101"),
            Array.AsReadOnly(new[]
            {
                new CiWorkflowStepObservation(1, "Tests", "completed", "success", "2026-10-30T20:00:00Z")
            }));
        return new CiWorkflowEvidencePacket("TrentBest/SimpleDemos", ".github/workflows/ci.yml",
            runId, 1, "refs/heads/development",
            "abcdef0123456789abcdef0123456789abcdef01", "push", workflowName,
            CiRunStatus.Completed, CiRunConclusion.Success,
            new Uri($"https://github.com/TrentBest/SimpleDemos/actions/runs/{runId}"),
            DateTimeOffset.Parse("2026-10-30T20:01:00Z"),
            Array.AsReadOnly(new[] { job }), true, true);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
