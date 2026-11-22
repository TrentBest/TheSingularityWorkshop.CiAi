using TheSingularityWorkshop.CiAi;

internal static class CiAiPipelineGateMembershipPolicyContractTests
{
    public static int Run()
    {
        var passed = 0;
        var shaB = new string('b', 40);
        var shaD = new string('d', 40);
        var b = new CiAiRepositoryGateReference("gate-b", "TrentBest/B", "candidate-b", shaB);
        var d = new CiAiRepositoryGateReference("gate-d", "TrentBest/D", "candidate-d", shaD);

        Check("repository membership in the named pipeline is sufficient", () =>
        {
            var policy = new CiAiPipelineGateMembershipPolicy(new Dictionary<string, IReadOnlyCollection<string>>
            {
                ["TrentBest/B"] = new[] { "PipelineA", "PipelineB" }
            });
            var result = policy.Evaluate(CiAiPipelineGateExpression.ForPipeline("PipelineA", b));
            Assert(result.IsAllowed && result.Errors.Count == 0, "pipeline member was rejected");
        });

        Check("unregistered repositories fail closed", () =>
        {
            var policy = new CiAiPipelineGateMembershipPolicy(
                new Dictionary<string, IReadOnlyCollection<string>>());
            var result = policy.Evaluate(CiAiPipelineGateExpression.ForPipeline("PipelineA", b));
            Assert(!result.IsAllowed && result.Errors.Contains("pipeline.repository-not-registered:TrentBest/B"),
                "unregistered repository was accepted");
        });

        Check("cross-pipeline access requires an explicit grant", () =>
        {
            var policy = new CiAiPipelineGateMembershipPolicy(new Dictionary<string, IReadOnlyCollection<string>>
            {
                ["TrentBest/D"] = new[] { "PipelineB" }
            });
            var result = policy.Evaluate(CiAiPipelineGateExpression.ForPipeline("PipelineA", d));
            Assert(!result.IsAllowed &&
                   result.Errors.Contains("pipeline.cross-pipeline-grant-required:TrentBest/D:PipelineA"),
                "cross-pipeline gate bypassed the explicit grant");
        });

        Check("exact candidate-scoped cross-pipeline grant permits access", () =>
        {
            var grant = Grant("grant-1", "TrentBest/D", "candidate-d", shaD, "PipelineB", "PipelineA");
            var policy = new CiAiPipelineGateMembershipPolicy(new Dictionary<string, IReadOnlyCollection<string>>
            {
                ["TrentBest/D"] = new[] { "PipelineB" }
            }, new[] { grant });
            var result = policy.Evaluate(CiAiPipelineGateExpression.ForPipeline("PipelineA", d));
            Assert(result.IsAllowed && result.Errors.Count == 0, "exact approved cross-pipeline grant was rejected");
        });

        Check("grant for a different candidate SHA cannot authorize this leaf", () =>
        {
            var grant = Grant("grant-stale", "TrentBest/D", "candidate-d", new string('e', 40), "PipelineB", "PipelineA");
            var policy = new CiAiPipelineGateMembershipPolicy(new Dictionary<string, IReadOnlyCollection<string>>
            {
                ["TrentBest/D"] = new[] { "PipelineB" }
            }, new[] { grant });
            var result = policy.Evaluate(CiAiPipelineGateExpression.ForPipeline("PipelineA", d));
            Assert(!result.IsAllowed, "stale-SHA grant was accepted");
        });

        Check("grant for another target pipeline cannot authorize this leaf", () =>
        {
            var grant = Grant("grant-target", "TrentBest/D", "candidate-d", shaD, "PipelineB", "PipelineC");
            var policy = new CiAiPipelineGateMembershipPolicy(new Dictionary<string, IReadOnlyCollection<string>>
            {
                ["TrentBest/D"] = new[] { "PipelineB" }
            }, new[] { grant });
            var result = policy.Evaluate(CiAiPipelineGateExpression.ForPipeline("PipelineA", d));
            Assert(!result.IsAllowed, "grant for a different target pipeline was accepted");
        });

        Check("grants must carry explicit policy and approver identity", () =>
        {
            Throws<ArgumentException>(() => new CiAiCrossPipelineGateGrant(
                "grant-invalid", "TrentBest/D", "candidate-d", shaD,
                "PipelineB", "PipelineA", "policy-1", 1, " ", DateTimeOffset.Parse("2026-11-22T00:00:00Z")));
            Throws<ArgumentOutOfRangeException>(() => new CiAiCrossPipelineGateGrant(
                "grant-invalid", "TrentBest/D", "candidate-d", shaD,
                "PipelineB", "PipelineA", "policy-1", 0, "human", DateTimeOffset.Parse("2026-11-22T00:00:00Z")));
        });

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS pipeline gate membership {passed}: {name}");
        }
    }

    private static CiAiCrossPipelineGateGrant Grant(
        string id, string repository, string candidateId, string sha, string source, string target) =>
        new(id, repository, candidateId, sha, source, target,
            "approved-cross-pipeline-policy", 1, "authenticated-human",
            DateTimeOffset.Parse("2026-11-22T00:00:00Z"));

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
