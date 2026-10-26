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
        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        static CiWorkflowPlanStepDraft Step(string id, string name, string domain, params string[] dependencies) =>
            new(id, name, domain, Array.AsReadOnly(dependencies), CiWorkflowStepCondition.AllDependenciesSucceeded);
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
