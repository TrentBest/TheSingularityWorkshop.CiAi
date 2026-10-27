using TheSingularityWorkshop.CiAi;

internal static class DependencyReleaseGateCoordinatorContractTests
{
    public static int Run()
    {
        var passed = 0;
        var root = Path.Combine(Path.GetTempPath(), "ciai-gate-coordinator-tests-" + Guid.NewGuid().ToString("N"));
        var upstreamSha = new string('b', 40);
        var candidate = new DependencyReleaseCandidate(
            "TrentBest/TheSingularityWorkshop.FSM_Serialization", new string('a', 40),
            "TheSingularityWorkshop.FSM_Serialization", "1.0.1", "serialization-1.0.1-candidate",
            new[]
            {
                new RequiredPublishedDependency("TrentBest/TheSingularityWorkshop.FSM_API", upstreamSha,
                    "TheSingularityWorkshop.FSM_API", "2.0.0")
            });

        try
        {
            var coordinator = new DependencyReleaseGateCoordinator(new FileDependencyReleaseCandidateStore(root));
            Check("registers candidate and waits when dependency evidence is absent", () =>
            {
                Assert(coordinator.Register(candidate) == DependencyCandidateStoreOutcome.Saved,
                    "candidate was not durably registered");
                var waiting = coordinator.Resume(candidate.CandidateId, Array.Empty<PublishedDependencyEvidence>());
                Assert(waiting.State == DependencyReleaseGateState.WaitingForPublishedDependencies &&
                    waiting.CommitSha == candidate.CommitSha,
                    "pending candidate was not preserved while waiting");
            });

            Check("resumes after coordinator recreation without retargeting candidate", () =>
            {
                var resumed = new DependencyReleaseGateCoordinator(new FileDependencyReleaseCandidateStore(root));
                var result = resumed.Resume(candidate.CandidateId, new[]
                {
                    new PublishedDependencyEvidence(
                        "TrentBest/TheSingularityWorkshop.FSM_API", upstreamSha,
                        "TheSingularityWorkshop.FSM_API", "2.0.0",
                        "workflow-run-123", 1, "https://api.nuget.org/v3/index.json", true, true)
                });
                Assert(result.CanVerifyConsumer && result.CommitSha == candidate.CommitSha &&
                    result.CandidateId == candidate.CandidateId,
                    "resumed coordinator did not evaluate the original candidate");
            });

            Check("refuses to resume an unknown candidate ID", () =>
            {
                var threw = false;
                try { coordinator.Resume("missing-candidate", Array.Empty<PublishedDependencyEvidence>()); }
                catch (KeyNotFoundException) { threw = true; }
                Assert(threw, "unknown candidate ID did not fail explicitly");
            });
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS dependency release coordinator {passed}: {name}");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
