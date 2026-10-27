using TheSingularityWorkshop.CiAi;

internal static class ConsumerVerificationContractTests
{
    public static int Run()
    {
        var passed = 0;
        var consumerSha = new string('a', 40);
        var upstreamSha = new string('b', 40);
        var candidate = new DependencyReleaseCandidate(
            "TrentBest/TheSingularityWorkshop.FSM_Serialization", consumerSha,
            "TheSingularityWorkshop.FSM_Serialization", "1.0.1", "serialization-1.0.1-candidate",
            new[]
            {
                new RequiredPublishedDependency("TrentBest/TheSingularityWorkshop.FSM_API", upstreamSha,
                    "TheSingularityWorkshop.FSM_API", "2.0.0")
            });

        Check("accepts exact package resolution and passing consumer validation", () =>
        {
            var result = ConsumerVerificationEvaluator.Evaluate(candidate, ValidEvidence(candidate));
            Assert(result.ReadyForReleaseReview, "complete matching consumer evidence did not pass");
            Assert(result.CommitSha == consumerSha && result.CandidateId == candidate.CandidateId,
                "verification result lost original candidate identity");
        });

        Check("blocks evidence for a different candidate commit", () =>
        {
            var result = ConsumerVerificationEvaluator.Evaluate(candidate,
                ValidEvidence(candidate) with { CommitSha = new string('c', 40) });
            Assert(result.State == ConsumerVerificationState.Blocked, "different consumer commit was accepted");
        });

        Check("blocks evidence for a different candidate ID", () =>
        {
            var result = ConsumerVerificationEvaluator.Evaluate(candidate,
                ValidEvidence(candidate) with { CandidateId = "another-candidate" });
            Assert(result.State == ConsumerVerificationState.Blocked, "different candidate ID was accepted");
        });

        Check("fails when exact dependency version was not resolved", () =>
        {
            var result = ConsumerVerificationEvaluator.Evaluate(candidate,
                ValidEvidence(candidate) with
                {
                    ResolvedPackages = new[]
                    {
                        new ResolvedPackageEvidence("TheSingularityWorkshop.FSM_API", "2.0.1",
                            "https://api.nuget.org/v3/index.json")
                    }
                });
            Assert(!result.ReadyForReleaseReview &&
                result.MissingPackages.SequenceEqual(new[] { "TheSingularityWorkshop.FSM_API@2.0.0" }),
                "wrong package version satisfied exact dependency");
        });

        Check("blocks package resolution from an insecure feed", () =>
        {
            var result = ConsumerVerificationEvaluator.Evaluate(candidate,
                ValidEvidence(candidate) with
                {
                    ResolvedPackages = new[]
                    {
                        new ResolvedPackageEvidence("TheSingularityWorkshop.FSM_API", "2.0.0",
                            "http://example.invalid/v3/index.json")
                    }
                });
            Assert(result.State == ConsumerVerificationState.Blocked, "insecure feed was accepted");
        });

        Check("blocks duplicate package resolution evidence", () =>
        {
            var package = new ResolvedPackageEvidence("TheSingularityWorkshop.FSM_API", "2.0.0",
                "https://api.nuget.org/v3/index.json");
            var result = ConsumerVerificationEvaluator.Evaluate(candidate,
                ValidEvidence(candidate) with { ResolvedPackages = new[] { package, package } });
            Assert(result.State == ConsumerVerificationState.Blocked, "ambiguous resolution was accepted");
        });

        Check("requires workflow run and attempt identity", () =>
        {
            var result = ConsumerVerificationEvaluator.Evaluate(candidate,
                ValidEvidence(candidate) with { WorkflowAttempt = 0 });
            Assert(result.State == ConsumerVerificationState.Blocked, "unattributed evidence was accepted");
        });

        Check("restore failure blocks release review", () =>
        {
            var result = ConsumerVerificationEvaluator.Evaluate(candidate,
                ValidEvidence(candidate) with { RestorePassed = false });
            Assert(!result.ReadyForReleaseReview, "failed restore passed verification");
        });

        Check("build failure blocks release review", () =>
        {
            var result = ConsumerVerificationEvaluator.Evaluate(candidate,
                ValidEvidence(candidate) with { BuildPassed = false });
            Assert(!result.ReadyForReleaseReview, "failed build passed verification");
        });

        Check("test failure blocks release review", () =>
        {
            var result = ConsumerVerificationEvaluator.Evaluate(candidate,
                ValidEvidence(candidate) with { TestsPassed = false });
            Assert(!result.ReadyForReleaseReview, "failed tests passed verification");
        });

        Check("readiness does not grant publish authorization", () =>
        {
            var result = ConsumerVerificationEvaluator.Evaluate(candidate, ValidEvidence(candidate));
            Assert(result.ReadyForReleaseReview &&
                result.GetType().GetProperty("PublishAuthorized") is null,
                "consumer verification exposed publication authorization");
        });

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS consumer verification {passed}: {name}");
        }
    }

    private static ConsumerVerificationEvidence ValidEvidence(DependencyReleaseCandidate candidate) => new(
        candidate.CandidateId, candidate.Repository, candidate.CommitSha,
        "40544176603", 1, true, true, true,
        new[]
        {
            new ResolvedPackageEvidence("TheSingularityWorkshop.FSM_API", "2.0.0",
                "https://api.nuget.org/v3/index.json")
        });

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
