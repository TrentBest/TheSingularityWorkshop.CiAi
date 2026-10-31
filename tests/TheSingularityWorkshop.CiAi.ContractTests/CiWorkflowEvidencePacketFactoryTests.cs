using TheSingularityWorkshop.CiAi;

internal static class CiWorkflowEvidencePacketFactoryTests
{
    private const string Repository = "TrentBest/TheSingularityWorkshop.CiAi";
    private const string WorkflowSha = "abcdef0123456789abcdef0123456789abcdef01";
    private const string CandidateSha = "123456789abcdef0123456789abcdef012345678";

    public static int Run()
    {
        var (runs, jobs) = CreateCollections();
        var evidence = new CiWorkflowCandidateCheckoutEvidence(
            "refs/heads/feature/candidate", CandidateSha,
            "contract",
            "Resolve optional branch target to an immutable commit",
            "Verify checkout provenance");
        var packet = CiWorkflowEvidencePacket.FromCollections(
            runs, jobs, 42, 2, DateTimeOffset.Parse("2026-10-31T00:30:00Z"), evidence);

        Assert(packet.WorkflowRef == "refs/heads/development" &&
            packet.WorkflowSha == WorkflowSha, "workflow-definition provenance was not sourced from the run collector");
        Assert(packet.CandidateRef == "refs/heads/feature/candidate" &&
            packet.CandidateSha == CandidateSha, "candidate provenance was not sourced from the checkout evidence");
        Assert(packet.CandidateCheckoutVerified && packet.IsSuccessful,
            "complete matching run/job observations did not produce successful candidate evidence");

        var ordinary = CiWorkflowEvidencePacket.FromCollections(
            CreateCollections().Runs, CreateCollections().Jobs, 42, 2,
            DateTimeOffset.Parse("2026-10-31T00:30:00Z"));
        Assert(ordinary.CandidateRef == ordinary.WorkflowRef &&
            ordinary.CandidateSha == ordinary.WorkflowSha && ordinary.CandidateCheckoutVerified,
            "ordinary run did not preserve the workflow event commit as its candidate");

        var wrongAttempt = CreateCollections(jobAttempt: 1);
        AssertThrows<InvalidDataException>(() => CiWorkflowEvidencePacket.FromCollections(
            wrongAttempt.Runs, wrongAttempt.Jobs, 42, 2, DateTimeOffset.UtcNow, evidence));

        var missingWorkflowRef = CreateCollections(workflowRef: null);
        AssertThrows<InvalidDataException>(() => CiWorkflowEvidencePacket.FromCollections(
            missingWorkflowRef.Runs, missingWorkflowRef.Jobs, 42, 2, DateTimeOffset.UtcNow, evidence));

        var truncatedRuns = CreateCollections(runTruncated: true, runTotalCount: 2);
        var incompleteDiscovery = CiWorkflowEvidencePacket.FromCollections(
            truncatedRuns.Runs, truncatedRuns.Jobs, 42, 2, DateTimeOffset.UtcNow, evidence);
        Assert(!incompleteDiscovery.DiscoveryComplete && !incompleteDiscovery.EvidenceComplete,
            "truncated run discovery was treated as complete evidence");

        var truncatedJobs = CreateCollections(jobTruncated: true, jobTotalCount: 2);
        var incompleteJobs = CiWorkflowEvidencePacket.FromCollections(
            truncatedJobs.Runs, truncatedJobs.Jobs, 42, 2, DateTimeOffset.UtcNow, evidence);
        Assert(!incompleteJobs.JobsComplete && !incompleteJobs.EvidenceComplete,
            "truncated job collection was treated as complete evidence");

        Console.WriteLine("PASS: 8 CI workflow evidence packet factory checks.");
        return 8;
    }

    private static (CiWorkflowRunCollection Runs, CiWorkflowJobCollection Jobs) CreateCollections(
        string? workflowRef = "refs/heads/development",
        int jobAttempt = 2,
        bool runTruncated = false,
        int runTotalCount = 1,
        bool jobTruncated = false,
        int jobTotalCount = 1)
    {
        var snapshot = new CiRunSnapshot(
            Repository, 42, "CiAi Contract Verification", WorkflowSha,
            CiRunStatus.Completed, CiRunConclusion.Success,
            new Uri($"https://github.com/{Repository}/actions/runs/42"),
            DateTimeOffset.Parse("2026-10-31T00:25:00Z"));
        var observation = new CiWorkflowRunObservation(
            snapshot, 2, ".github/workflows/ci.yml", "workflow_dispatch",
            Array.Empty<int>()) { WorkflowRef = workflowRef };
        var runCollection = new CiWorkflowRunCollection(
            Repository, WorkflowSha, new[] { observation }, 1, runTotalCount, runTruncated);

        var steps = new[]
        {
            new CiWorkflowStepObservation(1, "Resolve optional branch target to an immutable commit", "completed", "success", "2026-10-31T00:01:00Z"),
            new CiWorkflowStepObservation(2, "Verify checkout provenance", "completed", "success", "2026-10-31T00:02:00Z"),
            new CiWorkflowStepObservation(3, "Build", "completed", "success", "2026-10-31T00:10:00Z")
        };
        var job = new CiWorkflowJobObservation(
            100, 42, jobAttempt, "contract", "completed", "success",
            new Uri($"https://github.com/{Repository}/actions/runs/42/job/100"),
            Array.AsReadOnly(steps));
        var jobCollection = new CiWorkflowJobCollection(
            Repository, 42, jobAttempt, new[] { job }, 1, jobTotalCount, jobTruncated);
        return (runCollection, jobCollection);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
