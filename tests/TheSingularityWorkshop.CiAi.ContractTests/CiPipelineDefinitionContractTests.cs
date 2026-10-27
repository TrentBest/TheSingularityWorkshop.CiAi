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



        Check("empty pipeline execution is not reported as successful", () =>
        {
            var definition = new CiPipelineBuilder("TrentBest/TheSingularityWorkshop.CiAi")
                .On(CiPipelineTrigger.Manual)
                .Build();
            var report = new CiPipelineExecutor().ExecuteAsync(definition,
                "abcdef0123456789abcdef0123456789abcdef01", "plan-empty").GetAwaiter().GetResult();
            Assert(report.Steps.Count == 0 && !report.IsSuccessful,
                "an empty recipe was treated as a successful CI run");
        });

        Check("executor runs safe steps in order and records outcomes", () =>
        {
            var order = new List<int>();
            var definition = new CiPipelineBuilder("TrentBest/TheSingularityWorkshop.CiAi")
                .On(CiPipelineTrigger.Manual)
                .Add<bool>("first", () => { order.Add(1); return true; })
                .Add<string>("second", () => { order.Add(2); return "ok"; })
                .Build();
            var report = new CiPipelineExecutor().ExecuteAsync(definition,
                "abcdef0123456789abcdef0123456789abcdef01", "plan-1").GetAwaiter().GetResult();
            Assert(order.SequenceEqual(new[] { 1, 2 }), "executor did not preserve recipe order");
            Assert(report.IsSuccessful && report.Steps.Count == 2, "successful steps were not reported");
        });

        Check("executor stops after false and skips later work", () =>
        {
            var laterCalled = false;
            var definition = new CiPipelineBuilder("TrentBest/TheSingularityWorkshop.CiAi")
                .On(CiPipelineTrigger.Manual)
                .Add<bool>("fail", () => false)
                .Add<bool>("later", () => { laterCalled = true; return true; })
                .Build();
            var report = new CiPipelineExecutor().ExecuteAsync(definition,
                "abcdef0123456789abcdef0123456789abcdef01", "plan-2").GetAwaiter().GetResult();
            Assert(!report.IsSuccessful && !laterCalled, "executor continued after a failed step");
            Assert(report.Steps[0].Outcome == CiStepOutcome.Failed && report.Steps[1].Outcome == CiStepOutcome.Skipped,
                "failure/skip states were not distinct");
        });

        Check("executor honors explicit failed step results and bounds diagnostics", () =>
        {
            var laterCalled = false;
            var longDiagnostic = new string('x', 3000);
            var definition = new CiPipelineBuilder("TrentBest/TheSingularityWorkshop.CiAi")
                .On(CiPipelineTrigger.Manual)
                .Add<CiStepResult>("typed failure", () => CiStepResult.Failure(longDiagnostic))
                .Add<bool>("later", () => { laterCalled = true; return true; })
                .Build();
            var report = new CiPipelineExecutor().ExecuteAsync(definition,
                "abcdef0123456789abcdef0123456789abcdef01", "plan-typed-failure").GetAwaiter().GetResult();
            Assert(!report.IsSuccessful && !laterCalled, "executor ignored an explicit failed result");
            Assert(report.Steps[0].Outcome == CiStepOutcome.Failed, "typed failure did not fail the step");
            Assert(report.Steps[0].Diagnostic!.Length <= 2048, "typed diagnostic exceeded the bound");
        });

        Check("executor accepts explicit successful step results", () =>
        {
            var definition = new CiPipelineBuilder("TrentBest/TheSingularityWorkshop.CiAi")
                .On(CiPipelineTrigger.Manual)
                .Add<CiStepResult>("typed success", () => CiStepResult.Success())
                .Build();
            var report = new CiPipelineExecutor().ExecuteAsync(definition,
                "abcdef0123456789abcdef0123456789abcdef01", "plan-typed-success").GetAwaiter().GetResult();
            Assert(report.IsSuccessful && report.Steps[0].Outcome == CiStepOutcome.Succeeded,
                "explicit successful result was not accepted");
        });

        Check("executor captures exceptions and blocks subsequent work", () =>
        {
            var laterCalled = false;
            var definition = new CiPipelineBuilder("TrentBest/TheSingularityWorkshop.CiAi")
                .On(CiPipelineTrigger.Manual)
                .Add<bool>("throws", () => throw new InvalidOperationException("expected failure"))
                .Add<bool>("later", () => { laterCalled = true; return true; })
                .Build();
            var report = new CiPipelineExecutor().ExecuteAsync(definition,
                "abcdef0123456789abcdef0123456789abcdef01", "plan-3").GetAwaiter().GetResult();
            Assert(!report.IsSuccessful && !laterCalled, "executor continued after an exception");
            Assert(report.Steps[0].Diagnostic!.Contains("expected failure", StringComparison.Ordinal),
                "exception diagnostic was not retained");
        });

        Check("executor refuses privileged steps even when definition declares approval", () =>
        {
            var called = false;
            var definition = new CiPipelineBuilder("TrentBest/TheSingularityWorkshop.CiAi")
                .On(CiPipelineTrigger.Manual)
                .Add<bool>("publish", () => { called = true; return true; },
                    capability: CiStepCapability.PublishPackage, requiresApproval: true)
                .Add<bool>("later", () => true)
                .Build();
            var report = new CiPipelineExecutor().ExecuteAsync(definition,
                "abcdef0123456789abcdef0123456789abcdef01", "plan-4").GetAwaiter().GetResult();
            Assert(!called && report.Steps[0].Outcome == CiStepOutcome.Blocked,
                "privileged delegate ran without the dedicated authorization protocol");
            Assert(report.Steps[1].Outcome == CiStepOutcome.Skipped, "work continued after a privileged step was blocked");
        });

        Check("required evidence evaluator passes only exact identity and passing state", () =>
        {
            var sha = "abcdef0123456789abcdef0123456789abcdef01";
            var result = CiRequiredEvidenceEvaluator.Evaluate("TrentBest/SimpleDemos", sha,
                new[] { new CiRequiredEvidence("build"), new CiRequiredEvidence("security") },
                new[] {
                    new CiEvidenceFact("build", ReleaseEvidenceState.Passed, "TrentBest/SimpleDemos", sha),
                    new CiEvidenceFact("security", ReleaseEvidenceState.Passed, "TrentBest/SimpleDemos", sha)
                });
            Assert(result.IsPassing, "complete exact passing evidence was rejected");
        });

        Check("required evidence evaluator fails closed on missing, failed and mismatched evidence", () =>
        {
            var sha = "abcdef0123456789abcdef0123456789abcdef01";
            var result = CiRequiredEvidenceEvaluator.Evaluate("TrentBest/SimpleDemos", sha,
                new[] { new CiRequiredEvidence("build"), new CiRequiredEvidence("security"), new CiRequiredEvidence("test") },
                new[] {
                    new CiEvidenceFact("build", ReleaseEvidenceState.Passed, "TrentBest/SimpleDemos", sha),
                    new CiEvidenceFact("security", ReleaseEvidenceState.Failed, "TrentBest/SimpleDemos", sha),
                    new CiEvidenceFact("test", ReleaseEvidenceState.Passed, "TrentBest/Other", sha)
                });
            Assert(!result.IsPassing && result.Errors.Count == 2, "missing/failed/mismatched evidence was not rejected");
        });

        Check("authorization is bound to candidate and plan and expires", () =>
        {
            var now = DateTimeOffset.Parse("2026-10-25T12:00:00Z");
            var auth = new CiActionAuthorization("TrentBest/SimpleDemos",
                "abcdef0123456789abcdef0123456789abcdef01", "Demo.Package", "1.2.3",
                new string('a', 64), "plan-5", "owner", now.AddMinutes(-1), now.AddMinutes(5));
            Assert(auth.Validate("TrentBest/SimpleDemos", "abcdef0123456789abcdef0123456789abcdef01",
                "Demo.Package", "1.2.3", new string('a', 64), "plan-5", now).Count == 0,
                "valid candidate-bound authorization rejected");
            Assert(auth.Validate("TrentBest/SimpleDemos", "abcdef0123456789abcdef0123456789abcdef01",
                "Demo.Package", "1.2.4", new string('a', 64), "plan-5", now).Contains("authorization.package-version-mismatch"),
                "authorization was replayable for a different package version");
            Assert(auth.Validate("TrentBest/SimpleDemos", "abcdef0123456789abcdef0123456789abcdef01",
                "Demo.Package", "1.2.3", new string('a', 64), "plan-5", now.AddMinutes(6)).Count > 0,
                "expired authorization was accepted");
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
