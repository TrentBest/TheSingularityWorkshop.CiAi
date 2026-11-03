using TheSingularityWorkshop.CiAi;

internal static class CiWorkflowPlanContractTests
{
    public static int Run()
    {
        var passed = 0;
        var valid = new CiWorkflowPlanDraft("TrentBest/TheSingularityWorkshop.CiAi", "release-check",
            new[]
            {
                Step("publish-check", "Publication authorization check", "publication", "candidate-verify"),
                Step("candidate-verify", "Verify candidate", "release-candidate", "package"),
                Step("package", "Pack candidate", "package"),
                Step("unit-test", "Run unit tests", "build-test")
            });

        Check("workflow plan resolves dependencies in deterministic topological order", () =>
        {
            var result = CiWorkflowPlanValidator.Validate(valid);
            Assert(result.IsValid, "valid plan was rejected: " + string.Join(",", result.Errors));
            Assert(result.TopologicalStepIds.SequenceEqual(new[] { "package", "candidate-verify", "publish-check", "unit-test" }),
                "topological order is not stable or dependencies were violated");
        });
        Check("workflow plan recipe is deterministic and side-effect free", () =>
        {
            var first = CiWorkflowPlanValidator.RenderRecipe(valid);
            var second = CiWorkflowPlanValidator.RenderRecipe(valid);
            Assert(first == second, "same plan rendered differently");
            Assert(first.IndexOf("package", StringComparison.Ordinal) < first.IndexOf("candidate-verify", StringComparison.Ordinal),
                "recipe rendered a dependency after its consumer");
        });
        Check("workflow plan rejects duplicate and malformed stable IDs", () =>
        {
            var draft = new CiWorkflowPlanDraft(valid.Repository, "Bad Plan",
                new[] { Step("Bad_ID", "One", "build-test"), Step("one", "Two", "build-test"), Step("one", "Three", "build-test") });
            var result = CiWorkflowPlanValidator.Validate(draft);
            Assert(!result.IsValid && result.Errors.Contains("plan.id-invalid") &&
                result.Errors.Any(e => e.StartsWith("step.id-invalid:", StringComparison.Ordinal)) &&
                result.Errors.Contains("step.id-duplicate:one"), "invalid IDs were not diagnosed");
        });
        Check("workflow plan rejects unknown domains and dependency references", () =>
        {
            var result = CiWorkflowPlanValidator.Validate(new CiWorkflowPlanDraft(valid.Repository, "bad-references",
                new[] { Step("build", "Build", "not-a-domain", "missing-step") }));
            Assert(!result.IsValid && result.Errors.Contains("step.domain-unknown:build") &&
                result.Errors.Contains("step.dependency-unknown:build:missing-step"), "invalid references were not diagnosed");
        });
        Check("workflow plan rejects self-dependencies and dependency cycles", () =>
        {
            var self = CiWorkflowPlanValidator.Validate(new CiWorkflowPlanDraft(valid.Repository, "self",
                new[] { Step("loop", "Loop", "build-test", "loop") }));
            var cycle = CiWorkflowPlanValidator.Validate(new CiWorkflowPlanDraft(valid.Repository, "cycle",
                new[] { Step("alpha", "Alpha", "build-test", "beta"), Step("beta", "Beta", "build-test", "alpha") }));
            Assert(self.Errors.Contains("step.self-dependency:loop"), "self-dependency was not diagnosed");
            Assert(cycle.Errors.Contains("plan.dependency-cycle"), "cycle was not diagnosed");
        });
        Check("invalid workflow plan cannot be rendered as a recipe", () =>
        {
            var invalid = new CiWorkflowPlanDraft(valid.Repository, "broken",
                new[] { Step("build", "Build", "build-test", "missing") });
            Throws<ArgumentException>(() => CiWorkflowPlanValidator.RenderRecipe(invalid));
        });
        Check("workflow plan rejects duplicate dependencies and invalid conditions", () =>
        {
            var result = CiWorkflowPlanValidator.Validate(new CiWorkflowPlanDraft(valid.Repository, "bad-conditions",
                new[] { new CiWorkflowPlanStepDraft("build", "Build", "build-test", new[] { "other", "other" },
                    (CiWorkflowStepCondition)99) }));
            Assert(result.Errors.Contains("step.dependency-duplicate:build") &&
                result.Errors.Contains("step.condition-invalid:build"), "invalid condition or duplicate dependency was not diagnosed");
        });
        Check("workflow scheduler keeps dependent work pending until prerequisites are observed", () =>
        {
            var plan = SchedulingPlan(valid.Repository);
            var report = CiWorkflowPlanScheduler.Evaluate(plan,
                new Dictionary<string, CiStepOutcome>(StringComparer.Ordinal));
            Assert(Decision(report, "build").Eligibility == CiWorkflowStepEligibility.Eligible,
                "root step was not immediately eligible");
            Assert(Decision(report, "test").Eligibility == CiWorkflowStepEligibility.Pending &&
                Decision(report, "cleanup").Eligibility == CiWorkflowStepEligibility.Pending,
                "dependent work became eligible before prerequisites completed");
        });
        Check("workflow scheduler applies success, always, failure, and completion conditions", () =>
        {
            var plan = SchedulingPlan(valid.Repository);
            var report = CiWorkflowPlanScheduler.Evaluate(plan, new Dictionary<string, CiStepOutcome>
            {
                ["build"] = CiStepOutcome.Succeeded,
                ["test"] = CiStepOutcome.Failed
            });
            Assert(Decision(report, "test").Eligibility == CiWorkflowStepEligibility.Completed &&
                Decision(report, "test").ObservedOutcome == CiStepOutcome.Failed,
                "completed step outcome was not preserved");
            Assert(Decision(report, "cleanup").Eligibility == CiWorkflowStepEligibility.Eligible,
                "Always condition did not run after a failed dependency");
            Assert(Decision(report, "notify").Eligibility == CiWorkflowStepEligibility.Eligible,
                "AnyDependencyFailed did not activate after a failure");
            Assert(Decision(report, "aggregate").Eligibility == CiWorkflowStepEligibility.Eligible,
                "AllDependenciesCompleted did not activate after terminal completion");
        });
        Check("workflow scheduler skips success-only and failure-only work when conditions are false", () =>
        {
            var plan = SchedulingPlan(valid.Repository);
            var report = CiWorkflowPlanScheduler.Evaluate(plan, new Dictionary<string, CiStepOutcome>
            {
                ["build"] = CiStepOutcome.Succeeded,
                ["test"] = CiStepOutcome.Succeeded
            });
            Assert(Decision(report, "cleanup").Eligibility == CiWorkflowStepEligibility.Eligible,
                "Always condition was not eligible after successful completion");
            Assert(Decision(report, "notify").Eligibility == CiWorkflowStepEligibility.Skipped,
                "AnyDependencyFailed ran when no dependency failed");
            Assert(Decision(report, "aggregate").Eligibility == CiWorkflowStepEligibility.Eligible,
                "AllDependenciesCompleted did not activate after successful completion");
        });
        Check("workflow scheduler preserves uncertainty instead of treating it as success or failure", () =>
        {
            var plan = SchedulingPlan(valid.Repository);
            var report = CiWorkflowPlanScheduler.Evaluate(plan, new Dictionary<string, CiStepOutcome>
            {
                ["build"] = CiStepOutcome.Indeterminate
            });
            Assert(Decision(report, "test").Eligibility == CiWorkflowStepEligibility.Indeterminate &&
                Decision(report, "test").ReasonCode == "dependency.outcome-uncertain",
                "uncertain dependency was not propagated as indeterminate");
        });
        Check("workflow scheduler rejects unknown step outcomes and invalid enum values", () =>
        {
            var plan = SchedulingPlan(valid.Repository);
            Throws<ArgumentException>(() => CiWorkflowPlanScheduler.Evaluate(plan,
                new Dictionary<string, CiStepOutcome> { ["not-in-plan"] = CiStepOutcome.Succeeded }));
            Throws<ArgumentOutOfRangeException>(() => CiWorkflowPlanScheduler.Evaluate(plan,
                new Dictionary<string, CiStepOutcome> { ["build"] = (CiStepOutcome)99 }));
        });
        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        static CiWorkflowPlanStepDraft Step(string id, string name, string domain, params string[] dependencies) =>
            new(id, name, domain, Array.AsReadOnly(dependencies), CiWorkflowStepCondition.AllDependenciesSucceeded);
        static CiWorkflowPlanDraft SchedulingPlan(string repository) =>
            new(repository, "condition-scheduling", new[]
            {
                Step("build", "Build", "build-test"),
                new CiWorkflowPlanStepDraft("test", "Test", "build-test", new[] { "build" }),
                new CiWorkflowPlanStepDraft("cleanup", "Cleanup", "recovery", new[] { "test" },
                    CiWorkflowStepCondition.Always),
                new CiWorkflowPlanStepDraft("notify", "Notify failure", "observability", new[] { "test" },
                    CiWorkflowStepCondition.AnyDependencyFailed),
                new CiWorkflowPlanStepDraft("aggregate", "Aggregate", "integration", new[] { "test" },
                    CiWorkflowStepCondition.AllDependenciesCompleted)
            });
        static CiWorkflowStepSchedulingDecision Decision(CiWorkflowPlanSchedulingReport report, string stepId) =>
            report.Steps.Single(step => step.StepId == stepId);
        static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException($"Expected {typeof(T).Name}.");
        }
    }
}
