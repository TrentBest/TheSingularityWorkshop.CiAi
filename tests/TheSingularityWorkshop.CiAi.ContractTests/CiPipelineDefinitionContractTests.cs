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
                .Add<CiStepResult>("second", () => { order.Add(2); return CiStepResult.Success(); })
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

        Check("executor supplies candidate context and prior step outputs", () =>
        {
            string? observed = null;
            var definition = new CiPipelineBuilder("TrentBest/TheSingularityWorkshop.CiAi")
                .On(CiPipelineTrigger.Manual)
                .Add<CiStepResult>("source", () => CiStepResult.Success("built"))
                .Add<CiStepResult>("consume", context =>
                {
                    observed = $"{context.Repository}|{context.CommitSha}|{context.PlanId}|{context.Values["source"]}";
                    return CiStepResult.Success();
                })
                .Build();
            var report = new CiPipelineExecutor().ExecuteAsync(definition,
                "abcdef0123456789abcdef0123456789abcdef01", "plan-context").GetAwaiter().GetResult();
            Assert(report.IsSuccessful, "context-aware operation did not succeed");
            Assert(observed == "TrentBest/TheSingularityWorkshop.CiAi|abcdef0123456789abcdef0123456789abcdef01|plan-context|built",
                "execution context did not expose the exact candidate and prior output");
        });

        Check("executor treats unsupported result types as indeterminate and stops", () =>
        {
            var laterCalled = false;
            var definition = new CiPipelineBuilder("TrentBest/TheSingularityWorkshop.CiAi")
                .On(CiPipelineTrigger.Manual)
                .Add<int>("unknown exit code", () => 0)
                .Add<bool>("later", () => { laterCalled = true; return true; })
                .Build();
            var report = new CiPipelineExecutor().ExecuteAsync(definition,
                "abcdef0123456789abcdef0123456789abcdef01", "plan-indeterminate").GetAwaiter().GetResult();
            Assert(!report.IsSuccessful && !laterCalled, "unknown result was accepted or execution continued");
            Assert(report.Steps[0].Outcome == CiStepOutcome.Indeterminate,
                "unsupported result did not produce an indeterminate outcome");
        });

        Check("executor reports pre-cancelled work without invoking delegates", () =>
        {
            var called = false;
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var definition = new CiPipelineBuilder("TrentBest/TheSingularityWorkshop.CiAi")
                .On(CiPipelineTrigger.Manual)
                .Add<bool>("not started", () => { called = true; return true; })
                .Add<bool>("also not started", () => true)
                .Build();
            var report = new CiPipelineExecutor().ExecuteAsync(definition,
                "abcdef0123456789abcdef0123456789abcdef01", "plan-cancelled", cancellation.Token).GetAwaiter().GetResult();
            Assert(!called && report.Steps[0].Outcome == CiStepOutcome.Cancelled &&
                report.Steps[1].Outcome == CiStepOutcome.Skipped,
                "cancellation did not stop work with distinct outcomes");
        });

        Check("non-generic task completion is a successful explicit completion", () =>
        {
            var definition = new CiPipelineBuilder("TrentBest/TheSingularityWorkshop.CiAi")
                .On(CiPipelineTrigger.Manual)
                .Add<Task>("async", () => Task.CompletedTask)
                .Build();
            var report = new CiPipelineExecutor().ExecuteAsync(definition,
                "abcdef0123456789abcdef0123456789abcdef01", "plan-async").GetAwaiter().GetResult();
            Assert(report.IsSuccessful, "completed non-generic task was not successful");
        });

        Check("canonical execution JSON excludes wall-clock timestamps", () =>
        {
            var definition = new CiPipelineBuilder("TrentBest/TheSingularityWorkshop.CiAi")
                .On(CiPipelineTrigger.Manual)
                .Add<CiStepResult>("safe", () => CiStepResult.Success())
                .Build();
            var executor = new CiPipelineExecutor();
            var first = executor.ExecuteAsync(definition, "abcdef0123456789abcdef0123456789abcdef01", "plan-stable").GetAwaiter().GetResult();
            var second = executor.ExecuteAsync(definition, "abcdef0123456789abcdef0123456789abcdef01", "plan-stable").GetAwaiter().GetResult();
            var firstJson = CiPipelineExecutionReportRenderer.RenderJson(first);
            var secondJson = CiPipelineExecutionReportRenderer.RenderJson(second);
            Assert(firstJson == secondJson, "identical outcomes produced different canonical JSON");
            Assert(firstJson.Contains("\"schema\": \"ciai.pipeline-execution.v1\"", StringComparison.Ordinal),
                "canonical report omitted its schema identity");
            Assert(!firstJson.Contains("StartedAt", StringComparison.Ordinal) &&
                !firstJson.Contains("FinishedAt", StringComparison.Ordinal),
                "canonical report included nondeterministic wall-clock values");
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
