using TheSingularityWorkshop.CiAi;

internal static class DependencyReleaseGateContractTests
{
    public static int Run()
    {
        var passed = 0;
        var shaA = new string('a', 40);
        var shaB = new string('b', 40);
        var candidate = new DependencyReleaseCandidate(
            "TrentBest/TheSingularityWorkshop.FSM_Serialization", shaA,
            "TheSingularityWorkshop.FSM_Serialization", "1.0.1", "serialization-1.0.1-candidate",
            new[]
            {
                new RequiredPublishedDependency("TrentBest/TheSingularityWorkshop.FSM_API", shaB,
                    "TheSingularityWorkshop.FSM_API", "2.0.0")
            });

        Check("preserves exact consumer candidate while upstream package is missing", () =>
        {
            var result = DependencyReleaseGateEvaluator.Evaluate(candidate, Array.Empty<PublishedDependencyEvidence>());
            Assert(result.State == DependencyReleaseGateState.WaitingForPublishedDependencies, "missing dependency did not pause candidate");
            Assert(result.CandidateId == candidate.CandidateId && result.CommitSha == shaA &&
                result.PackageVersion == "1.0.1", "candidate identity changed while waiting");
            Assert(result.MissingDependencyEvidence.SequenceEqual(new[] { "TheSingularityWorkshop.FSM_API@2.0.0" }),
                "missing exact package was not identified");
        });

        Check("resumes same candidate after exact package is verified published", () =>
        {
            var result = DependencyReleaseGateEvaluator.Evaluate(candidate, new[] { ValidEvidence(shaB) });
            Assert(result.CanVerifyConsumer, "verified package did not open consumer verification gate");
            Assert(result.CommitSha == shaA && result.CandidateId == "serialization-1.0.1-candidate",
                "resume retargeted the original candidate");
        });

        Check("blocks evidence for a different upstream source commit", () =>
        {
            var result = DependencyReleaseGateEvaluator.Evaluate(candidate, new[] { ValidEvidence(new string('c', 40)) });
            Assert(result.State == DependencyReleaseGateState.Blocked &&
                result.Errors.Any(e => e.StartsWith("dependency.upstream-identity-mismatch:", StringComparison.Ordinal)),
                "wrong upstream commit was accepted");
        });

        Check("blocks upstream validation failure even if package exists", () =>
        {
            var result = DependencyReleaseGateEvaluator.Evaluate(candidate, new[] { ValidEvidence(shaB) with { UpstreamValidationPassed = false } });
            Assert(result.State == DependencyReleaseGateState.Blocked, "failed upstream validation was accepted");
        });

        Check("blocks when exact package is not found on feed", () =>
        {
            var result = DependencyReleaseGateEvaluator.Evaluate(candidate, new[] { ValidEvidence(shaB) with { PackageFound = false } });
            Assert(result.State == DependencyReleaseGateState.Blocked, "unpublished package was accepted");
        });

        Check("blocks published-package evidence from an insecure HTTP feed", () =>
        {
            var result = DependencyReleaseGateEvaluator.Evaluate(candidate,
                new[] { ValidEvidence(shaB) with { FeedUri = "http://api.nuget.org/v3/index.json" } });
            Assert(result.State == DependencyReleaseGateState.Blocked &&
                result.Errors.Any(e => e.StartsWith("dependency.feed-identity-invalid:", StringComparison.Ordinal)),
                "insecure package feed was accepted");
        });

        Check("matches NuGet package IDs without case sensitivity", () =>
        {
            var result = DependencyReleaseGateEvaluator.Evaluate(candidate,
                new[] { ValidEvidence(shaB) with { PackageId = "thesingularityworkshop.fsm_api" } });
            Assert(result.CanVerifyConsumer, "package ID casing prevented exact package evidence from matching");
        });

        Check("blocks duplicate ambiguous evidence", () =>
        {
            var item = ValidEvidence(shaB);
            var result = DependencyReleaseGateEvaluator.Evaluate(candidate, new[] { item, item });
            Assert(result.State == DependencyReleaseGateState.Blocked, "ambiguous duplicate evidence was accepted");
        });

        Check("blocks evidence without workflow attempt identity", () =>
        {
            var result = DependencyReleaseGateEvaluator.Evaluate(candidate, new[] { ValidEvidence(shaB) with { WorkflowAttempt = 0 } });
            Assert(result.State == DependencyReleaseGateState.Blocked, "evidence without workflow attempt was accepted");
        });

        Check("ready means consumer verification only, never publication authorization", () =>
        {
            var result = DependencyReleaseGateEvaluator.Evaluate(candidate, new[] { ValidEvidence(shaB) });
            Assert(result.CanVerifyConsumer && result.State != DependencyReleaseGateState.Blocked,
                "gate failed to separate readiness from later consumer verification");
            Assert(result.GetType().GetProperty("PublishAuthorized") is null,
                "dependency gate must not expose or imply publish authorization");
        });

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS dependency release gate {passed}: {name}");
        }
    }

    private static PublishedDependencyEvidence ValidEvidence(string upstreamSha) => new(
        "TrentBest/TheSingularityWorkshop.FSM_API", upstreamSha,
        "TheSingularityWorkshop.FSM_API", "2.0.0", "40537857547", 1,
        "https://api.nuget.org/v3/index.json", true, true);

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
