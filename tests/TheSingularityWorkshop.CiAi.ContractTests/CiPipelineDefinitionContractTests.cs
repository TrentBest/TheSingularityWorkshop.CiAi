using TheSingularityWorkshop.CiAi;

internal static class CiPipelineDefinitionContractTests
{
    public static int Run()
    {
        var passed = 0;
        Check("pipeline definition never invokes registered delegates", () =>
        {
            var calls = 0;
            var definition = new CiPipelineBuilder("TrentBest/TheSingularityWorkshop.CiAi")
                .On(CiPipelineTrigger.PullRequest)
                .Add<bool>("Publish NuGet", () => { calls++; return true; },
                    capability: CiStepCapability.PublishPackage, requiresApproval: true)
                .Build();

            _ = definition.ToRecipe();
            _ = definition.Steps[0].Name;
            Assert(calls == 0, "definition or rendering invoked a registered operation");
        });

        Check("recipe rendering is deterministic and preserves step order", () =>
        {
            var definition = new CiPipelineBuilder("TrentBest/TheSingularityWorkshop.CiAi")
                .On(CiPipelineTrigger.PullRequest)
                .On(CiPipelineTrigger.PushToMain)
                .Add<bool>("Restore", () => true)
                .Add<bool>("Test", () => true)
                .Build();
            var expected = definition.ToRecipe();
            Assert(expected == definition.ToRecipe(), "same definition rendered differently");
            Assert(expected.IndexOf("Restore", StringComparison.Ordinal) < expected.IndexOf("Test", StringComparison.Ordinal),
                "recipe did not preserve step order");
            Assert(definition.Triggers.Count == 2, "triggers were not preserved");
        });

        Check("duplicate step IDs are rejected", () =>
        {
            var builder = new CiPipelineBuilder("TrentBest/TheSingularityWorkshop.CiAi")
                .On(CiPipelineTrigger.Manual)
                .Add<bool>("Build", () => true, id: "compile");
            Throws<ArgumentException>(() => builder.Add<bool>("Second build", () => true, id: "COMPILE"));
        });

        Check("privileged steps require explicit approval metadata", () =>
        {
            var builder = new CiPipelineBuilder("TrentBest/TheSingularityWorkshop.CiAi")
                .On(CiPipelineTrigger.PushToMain);
            Throws<ArgumentException>(() => builder.Add<bool>("Publish NuGet", () => true,
                capability: CiStepCapability.PublishPackage));
            var definition = builder.Add<bool>("Publish NuGet", () => true,
                capability: CiStepCapability.PublishPackage, requiresApproval: true).Build();
            Assert(definition.Steps[0].RequiresApproval, "approval gate was not represented");
            Assert(definition.ToRecipe().Contains("approval required: PublishPackage", StringComparison.Ordinal),
                "recipe did not make approval requirement visible");
        });

        Check("approval cannot be requested without a privileged capability", () =>
        {
            var builder = new CiPipelineBuilder("TrentBest/TheSingularityWorkshop.CiAi")
                .On(CiPipelineTrigger.Manual);
            Throws<ArgumentException>(() => builder.Add<bool>("Ordinary step", () => true, requiresApproval: true));
        });

        Check("pipeline requires an explicit trigger", () =>
        {
            Throws<InvalidOperationException>(() => new CiPipelineBuilder("TrentBest/TheSingularityWorkshop.CiAi").Build());
        });

        Check("built builder cannot be mutated or built twice", () =>
        {
            var builder = new CiPipelineBuilder("TrentBest/TheSingularityWorkshop.CiAi")
                .On(CiPipelineTrigger.Manual);
            _ = builder.Build();
            Throws<InvalidOperationException>(() => builder.Add<bool>("Late step", () => true));
            Throws<InvalidOperationException>(() => builder.Build());
        });

        return passed;

        void Check(string name, Action action)
        {
            try
            {
                action();
                passed++;
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
                throw;
            }
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
