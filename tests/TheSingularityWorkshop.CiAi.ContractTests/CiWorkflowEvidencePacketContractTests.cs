using TheSingularityWorkshop.CiAi;

internal static class CiWorkflowEvidencePacketContractTests
{
    private const string Sha = "abcdef0123456789abcdef0123456789abcdef01";
    private const string WorkflowSha = "1234567890abcdef1234567890abcdef12345678";

    public static int Run()
    {
        var success = CreatePacket();
        Assert(success.ContractVersion == 2 && success.Schema == "ciai.workflow-evidence.v2",
            "packet schema/version was not fixed");
        Assert(success.EvidenceComplete && success.IsSuccessful && success.BlockingReasons.Count == 0,
            "complete successful evidence did not pass");
        Assert(success.WorkflowSha == Sha && success.CandidateSha == Sha &&
            success.RunAttempt == 2 && success.WorkflowRef == "refs/heads/agent/fix" &&
            success.CandidateRef == "refs/heads/agent/fix" && success.CandidateCheckoutVerified,
            "ordinary run workflow/candidate provenance was not retained");

        var json = success.ToJson();
        Assert(json.Contains("\"repository\": \"TrentBest/SimpleDemos\"", StringComparison.Ordinal) &&
            json.Contains("\"runAttempt\": 2", StringComparison.Ordinal) &&
            json.Contains("\"workflowSha\": \"" + Sha + "\"", StringComparison.Ordinal) &&
            json.Contains("\"candidateSha\": \"" + Sha + "\"", StringComparison.Ordinal),
            "serialized packet omitted its exact workflow/candidate provenance");
        var roundTrip = CiWorkflowEvidencePacket.FromJson(json);
        Assert(roundTrip.IsSuccessful && roundTrip.RunId == success.RunId &&
            roundTrip.Jobs[0].Steps[0].Name == "Run tests" &&
            roundTrip.WorkflowSha == Sha && roundTrip.CandidateSha == Sha,
            "JSON round-trip lost packet or step evidence");

        var candidate = CreateCandidatePacket();
        Assert(candidate.WorkflowRef == CiWorkflowDispatchInputContract.TrustedWorkflowRef &&
            candidate.WorkflowSha == WorkflowSha &&
            candidate.CandidateRef == "refs/heads/feature/agent-fix" &&
            candidate.CandidateSha == Sha && candidate.CandidateCheckoutVerified &&
            candidate.IsSuccessful,
            "trusted workflow and feature candidate were not independently verified");
        var candidateJson = candidate.ToJson();
        Assert(candidateJson.Contains("\"workflowRef\": \"refs/heads/development\"", StringComparison.Ordinal) &&
            candidateJson.Contains("\"candidateRef\": \"refs/heads/feature/agent-fix\"", StringComparison.Ordinal) &&
            candidateJson.Contains("\"candidateCheckoutVerified\": true", StringComparison.Ordinal),
            "v2 JSON did not preserve both SHA roles and checkout verification");
        var candidateRoundTrip = CiWorkflowEvidencePacket.FromJson(candidateJson);
        Assert(candidateRoundTrip.CandidateCheckoutVerified &&
            candidateRoundTrip.CandidateSha == Sha &&
            candidateRoundTrip.WorkflowSha == WorkflowSha,
            "candidate provenance was lost on JSON round-trip");

        var unverifiedCandidate = CreateCandidatePacket(verificationStepConclusion: "skipped");
        Assert(unverifiedCandidate.EvidenceComplete && !unverifiedCandidate.CandidateCheckoutVerified &&
            !unverifiedCandidate.IsSuccessful &&
            unverifiedCandidate.BlockingReasons.Contains("candidate.checkout-unverified"),
            "candidate without successful checkout provenance was accepted");

        var incomplete = CreatePacket(discoveryComplete: false);
        Assert(!incomplete.EvidenceComplete && !incomplete.IsSuccessful &&
            incomplete.BlockingReasons.Contains("evidence.discovery-incomplete"),
            "incomplete discovery was treated as complete evidence");

        // A raw failed step is not enough to infer a failed job: GitHub may report
        // continue-on-error or other policy-specific semantics at the job level.
        var failedStep = CreatePacket(stepConclusion: "failure");
        Assert(failedStep.EvidenceComplete && failedStep.IsSuccessful &&
            failedStep.Jobs[0].Steps[0].Conclusion == "failure",
            "raw step outcome was not preserved independently from the successful job conclusion");

        var failedJob = CreatePacket(jobConclusion: "failure", runConclusion: CiRunConclusion.Success);
        Assert(!failedJob.IsSuccessful && failedJob.BlockingReasons.Contains("job.101.not-successful"),
            "unsuccessful job was accepted despite a successful workflow conclusion");

        Throws<ArgumentException>(() => CreatePacket(jobAttempt: 1));
        Throws<ArgumentException>(() => CreatePacket(jobRunId: 43));
        Throws<ArgumentException>(() => CreatePacket(runUrl: new Uri("https://example.com/run/42")));
        Throws<ArgumentException>(() => CreatePacket(workflowPath: "../build.yml"));
        Throws<ArgumentException>(() => CreateCandidatePacket(workflowRef: "refs/heads/feature/untrusted"));
        Throws<ArgumentException>(() => new CiWorkflowCandidateCheckoutEvidence(
            "refs/heads/master", Sha, "contract", "Resolve candidate", "Verify checkout"));
        Throws<InvalidDataException>(() => CiWorkflowEvidencePacket.FromJson(
            json.Replace("ciai.workflow-evidence.v2", "ciai.workflow-evidence.v99", StringComparison.Ordinal)));
        Throws<InvalidDataException>(() => CiWorkflowEvidencePacket.FromJson(
            json.Replace("\"contractVersion\": 2", "\"contractVersion\": 1", StringComparison.Ordinal)
                .Replace("ciai.workflow-evidence.v2", "ciai.workflow-evidence.v1", StringComparison.Ordinal)));

        Console.WriteLine("PASS: 20 CI workflow evidence packet v2 contract checks.");
        return 20;
    }

    private static CiWorkflowEvidencePacket CreatePacket(
        bool discoveryComplete = true,
        string stepConclusion = "success",
        string jobConclusion = "success",
        CiRunConclusion? runConclusion = null,
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
                "completed", jobConclusion, new Uri("https://github.com/TrentBest/SimpleDemos/actions/runs/42/job/101"),
                Array.AsReadOnly(steps))
        };
        return new CiWorkflowEvidencePacket(
            "TrentBest/SimpleDemos", workflowPath, 42, 2, "refs/heads/agent/fix", Sha,
            "pull_request", "CI", CiRunStatus.Completed,
            runConclusion ?? (jobConclusion == "success" ? CiRunConclusion.Success : CiRunConclusion.Failure),
            runUrl ?? new Uri("https://github.com/TrentBest/SimpleDemos/actions/runs/42"),
            DateTimeOffset.Parse("2026-10-30T20:01:00Z"),
            Array.AsReadOnly(jobs), discoveryComplete, jobsComplete: true);
    }

    private static CiWorkflowEvidencePacket CreateCandidatePacket(
        string workflowRef = "refs/heads/development",
        string verificationStepConclusion = "success")
    {
        var steps = new[]
        {
            new CiWorkflowStepObservation(1, "Resolve optional branch target to an immutable commit",
                "completed", "success", "2026-10-31T00:00:01Z"),
            new CiWorkflowStepObservation(2, "Verify checkout provenance",
                "completed", verificationStepConclusion, "2026-10-31T00:00:02Z")
        };
        var jobs = new[]
        {
            new CiWorkflowJobObservation(101, 42, 2, "contract", "completed", "success",
                new Uri("https://github.com/TrentBest/SimpleDemos/actions/runs/42/job/101"),
                Array.AsReadOnly(steps))
        };
        var candidateEvidence = new CiWorkflowCandidateCheckoutEvidence(
            "refs/heads/feature/agent-fix", Sha, "contract",
            "Resolve optional branch target to an immutable commit", "Verify checkout provenance");
        return new CiWorkflowEvidencePacket(
            "TrentBest/SimpleDemos", ".github/workflows/ci.yml", 42, 2,
            workflowRef, WorkflowSha, "workflow_dispatch", "CI",
            CiRunStatus.Completed, CiRunConclusion.Success,
            new Uri("https://github.com/TrentBest/SimpleDemos/actions/runs/42"),
            DateTimeOffset.Parse("2026-10-31T00:00:03Z"),
            Array.AsReadOnly(jobs), true, true,
            candidateCheckoutEvidence: candidateEvidence);
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
