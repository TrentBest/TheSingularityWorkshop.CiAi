using TheSingularityWorkshop.CiAi;

internal static class CiAiRepositoryBuilderContractTests
{
    private static int _passed;

    public static int Run()
    {
        _passed = 0;

        Check("direct dependencies retain exact repository identity", () =>
        {
            var definition = CiAi.ForRepository("TrentBest/A")
                .WithDependency("TrentBest/B")
                .Build();
            Assert(definition.Dependencies.Count == 1, "direct dependency was not retained");
            Assert(definition.DependencyRepositories.SequenceEqual(new[] { "TrentBest/B" }),
                "dependency repository identity changed");
        });

        Check("ordered fallback preserves D then E then F", () =>
        {
            var definition = CiAi.ForRepository("TrentBest/C")
                .WithDependency(CiAi.Logic.Or("TrentBest/D", "TrentBest/E", "TrentBest/F"))
                .Build();
            var fallback = definition.Dependencies.Single() as CiAiOrderedDependencyFallback;
            Assert(fallback is not null, "Or did not produce an ordered fallback");
            Assert(fallback!.Repositories.SequenceEqual(new[]
                { "TrentBest/D", "TrentBest/E", "TrentBest/F" }),
                "fallback order changed");
            Assert(definition.DependencyRepositories.SequenceEqual(
                new[] { "TrentBest/D", "TrentBest/E", "TrentBest/F" }),
                "candidate providers were not exposed in declared order");
        });

        Check("unnamed repositories join the default development pipeline", () =>
        {
            var definition = CiAi.ForRepository("TrentBest/A").Build();
            Assert(definition.NamedDevelopmentPipelines.Count == 0, "default membership was stored as named membership");
            Assert(definition.DevelopmentPipelines.SequenceEqual(new[] { CiAiRepositoryDefinition.DefaultDevelopmentPipeline }),
                "unnamed repository did not resolve to the default pipeline");
        });

        Check("named development pipeline membership is explicit", () =>
        {
            var definition = CiAi.ForRepository("TrentBest/A")
                .WithNamedDevelopmentPipeline("PipelineA")
                .Build();
            Assert(definition.DevelopmentPipelines.SequenceEqual(new[] { "PipelineA" }), "named pipeline was not preserved");
        });

        Check("a repository can explicitly join multiple named pipelines without duplicate membership", () =>
        {
            var definition = CiAi.ForRepository("TrentBest/A")
                .WithNamedDevelopmentPipeline("PipelineA")
                .WithNamedDevelopmentPipeline("PipelineB")
                .WithNamedDevelopmentPipeline("PipelineA")
                .Build();
            Assert(definition.DevelopmentPipelines.SequenceEqual(new[] { "PipelineA", "PipelineB" }),
                "named pipeline memberships were lost, reordered, or duplicated");
        });

        Check("ADP is opt-in and does not gate ordinary CI composition", () =>
        {
            var ordinary = CiAi.ForRepository("TrentBest/A").WithDependency("TrentBest/B").Build();
            var autonomous = CiAi.ForRepository("TrentBest/A")
                .WithDependency("TrentBest/B")
                .WithADP(new CiAiAdpSettings(maximumRepairAttempts: 2))
                .Build();
            Assert(!ordinary.AutonomousDevelopmentEnabled && ordinary.AdpSettings is null,
                "ordinary CI unexpectedly enabled ADP");
            Assert(autonomous.AutonomousDevelopmentEnabled &&
                autonomous.AdpSettings!.MaximumRepairAttempts == 2 &&
                autonomous.AdpSettings.RequireHumanApprovalForWrites,
                "explicit ADP settings were not preserved");
        });

        Check("LLM allocation carries provider model concurrency call token window and priority budgets", () =>
        {
            var definition = CiAi.ForRepository("TrentBest/A")
                .WithLLMAllocation(allocation => allocation
                    .SetProvider("OpenAI")
                    .SetModel("gpt")
                    .SetMaximumConcurrentCalls(2)
                    .SetMaximumCallsPerWindow(30)
                    .SetMaximumTokensPerWindow(50_000)
                    .SetWindow(TimeSpan.FromMinutes(15))
                    .SetPriority(CiAiLlmPriority.High))
                .Build();
            var allocation = definition.LlmAllocation!;
            Assert(allocation.Provider == "OpenAI" && allocation.Model == "gpt",
                "provider/model identity changed");
            Assert(allocation.MaximumConcurrentCalls == 2 &&
                allocation.MaximumCallsPerWindow == 30 &&
                allocation.MaximumTokensPerWindow == 50_000 &&
                allocation.Window == TimeSpan.FromMinutes(15) &&
                allocation.Priority == CiAiLlmPriority.High,
                "LLM capacity allocation changed");
        });

        Check("invalid dependency identities and self-dependencies fail closed", () =>
        {
            Throws<ArgumentException>(() => CiAi.ForRepository("TrentBest/A").WithDependency("B"));
            Throws<ArgumentException>(() => CiAi.ForRepository("TrentBest/A").WithDependency("TrentBest/A"));
            Throws<ArgumentException>(() => CiAi.Logic.Or("TrentBest/D", "TrentBest/D"));
            Throws<ArgumentException>(() => CiAi.Logic.Or("TrentBest/D"));
            Throws<ArgumentException>(() => CiAi.ForRepository("TrentBest/A")
                .WithDependency(CiAi.Logic.Or("TrentBest/A", "TrentBest/B")));
        });

        Check("duplicate dependency declarations and post-build mutation are rejected", () =>
        {
            var builder = CiAi.ForRepository("TrentBest/A").WithDependency("TrentBest/B");
            Throws<ArgumentException>(() => builder.WithDependency("TrentBest/B"));
            _ = builder.Build();
            Throws<InvalidOperationException>(() => builder.WithDependency("TrentBest/C"));
        });

        Check("invalid ADP and LLM budget values are rejected", () =>
        {
            Throws<ArgumentOutOfRangeException>(() => new CiAiAdpSettings(maximumRepairAttempts: 11));
            Throws<ArgumentOutOfRangeException>(() => CiAi.ForRepository("TrentBest/A")
                .WithLLMAllocation(a => a.SetMaximumConcurrentCalls(0)));
            Throws<ArgumentOutOfRangeException>(() => CiAi.ForRepository("TrentBest/A")
                .WithLLMAllocation(a => a.SetMaximumTokensPerWindow(0)));
            Throws<ArgumentOutOfRangeException>(() => CiAi.ForRepository("TrentBest/A")
                .WithLLMAllocation(a => a.SetPriority((CiAiLlmPriority)99)));
            Throws<InvalidOperationException>(() => CiAi.ForRepository("TrentBest/A")
                .WithLLMAllocation(_ => { }));
        });

        return _passed;
    }

    private static void Check(string name, Action action)
    {
        try { action(); _passed++; }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"CiAi repository builder contract failed: {name}", exception);
        }
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
