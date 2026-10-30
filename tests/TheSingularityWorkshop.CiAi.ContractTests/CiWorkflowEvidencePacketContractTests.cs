using TheSingularityWorkshop.CiAi;

internal static class CiWorkflowEvidencePacketContractTests
{
    private const string Sha = "abcdef0123456789abcdef0123456789abcdef01";

    public static int Run()
    {
        var success = CreatePacket();
        Assert(success.ContractVersion == 1 && success.Schema == "ciai.workflow-evidence.v1",
            "packet schema/version was not fixed");
        Assert(success.EvidenceComplete && success.IsSuccessful && success.BlockingReasons.Count == 0,
            "complete successful evidence did not pass");
        Assert(success.HeadSha == Sha && success.RunAttempt == 2 && success.HeadRef == "refs/heads/agent/fix",
            "candidate and attempt provenance was not retained");

        var json = success.ToJson();
        Assert(json.Contains("\"repository\": \"TrentBest/SimpleDemos\"", StringComparison.Ordinal) &&
            json.Contains("\"runAttempt\": 2", StringComparison.Ordinal) &&
            json.Contains(Sha, StringComparison.Ordinal),
            "serialized packet omitted its exact provenance");
        var roundTrip = CiWorkflowEvidencePacket.FromJson(json);
        Assert(roundTrip.IsSuccessful && roundTrip.RunId == success.RunId &&
            roundTrip.Jobs[0].Steps[0].Name == "Run tests",
            "JSON round-trip lost packet or step evidence");

        var incomplete = CreatePacket(discoveryComplete: false);
        Assert(!incomplete.EvidenceComplete && !incomplete.IsSuccessful &&
            incomplete.BlockingReasons.Contains("evidence.discovery-incomplete"),
            "incomplete discovery was treated as complete evidence");

        var failedStep = CreatePacket(stepConclusion: "failure");
        Assert(failedStep.EvidenceComplete && !failedStep.IsSuccessful &&
            failedStep.BlockingReasons.Contains("job.101.step.1.not-successful"),
            "failed step was not preserved as a blocking diagnostic");

        Throws<ArgumentException>(() => CreatePacket(jobAttempt: 1));
        Throws<ArgumentException>(() => CreatePacket(jobRunId: 43));
        Throws<ArgumentException>(() => CreatePacket(runUrl: new Uri("https://example.com/run/42")));
        Throws<ArgumentException>(() => CreatePacket(workflowPath: "../build.yml"));
        Throws<InvalidDataException>(() => CiWorkflowEvidencePacket.FromJson(
            json.Replace("ciai.workflow-evidence.v1", "ciai.workflow-evidence.v99", StringComparison.Ordinal)));

        Console.WriteLine("PASS: 10 CI workflow evidence packet contract checks.");
        return 10;
    }

    private static CiWorkflowEvidencePacket CreatePacket(
        bool discoveryComplete = true,
        string stepConclusion = "success",
        int jobAttempt = 2,
        long jobRunId = 42,
        Uri? runUrl = null,
        string workflowPath = ".github/workflows/ci.yml")
    {
        var steps = new[]
        {
            new CiWorkflowStepObservation(1, "Run tests", "completed", stepConclusion, "2026-10-30T20:00:00Z")
        };
        var jobs = new[]
        {
            new CiWorkflowJobObservation(101, jobRunId, jobAttempt, "build-and-test",
                "completed", stepConclusion, new Uri("https://github.com/TrentBest/SimpleDemos/actions/runs/42/job/101"),
                Array.AsReadOnly(steps))
        };
        return new CiWorkflowEvidencePacket(
            "TrentBest/SimpleDemos", workflowPath, 42, 2, "refs/heads/agent/fix", Sha,
            "pull_request", "CI", CiRunStatus.Completed,
            stepConclusion == "success" ? CiRunConclusion.Success : CiRunConclusion.Failure,
            runUrl ?? new Uri("https://github.com/TrentBest/SimpleDemos/actions/runs/42"),
            DateTimeOffset.Parse("2026-10-30T20:01:00Z"),
            Array.AsReadOnly(jobs), discoveryComplete, jobsComplete: true);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
