using TheSingularityWorkshop.CiAi;

internal static class CiPagesPublicationPlanContractTests
{
    public static int Run()
    {
        var passed = 0;
        var valid = new CiPagesPublicationPlanDraft(
            "TrentBest/TheSingularityWorkshop.CiAi", "site", "development");

        Check("Pages declaration expands into deterministic validated workflow plan", () =>
        {
            var first = CiPagesPublicationPlan.Expand(valid);
            var second = CiPagesPublicationPlan.Expand(valid);
            Assert(first.IsValid && first.Workflow is not null, "valid Pages declaration was rejected");
            Assert(CiWorkflowPlanValidator.RenderRecipe(first.Workflow!) ==
                   CiWorkflowPlanValidator.RenderRecipe(second.Workflow!), "Pages plan expansion was not deterministic");
            Assert(CiWorkflowPlanValidator.Validate(first.Workflow!).TopologicalStepIds.SequenceEqual(
                new[] { "build-site", "validate-site", "package-site", "deploy-pages" }),
                "Pages plan order or dependency chain was incorrect");
        });

        Check("Pages destination defaults to automatic publication without human approval", () =>
        {
            var expansion = CiPagesPublicationPlan.Expand(valid);
            Assert(expansion.PublicationPolicy.Mode == CiPublicationMode.Automatic &&
                   expansion.PublicationPolicy.Approval == CiPublicationApproval.None,
                "Pages policy incorrectly requires approval or is not automatic");
            Assert(expansion.PublicationPolicy.Validate().Count == 0, "Pages policy is invalid");
        });

        Check("Pages plan preserves development source branch and explicit triggers", () =>
        {
            var workflow = CiPagesPublicationPlan.Expand(valid).Workflow!;
            Assert(workflow.Triggers!.SequenceEqual(new[] { "push:development", "workflow_dispatch" }),
                "Pages plan did not preserve source branch triggers");
        });

        Check("Pages source path rejects traversal, absolute, and platform-dependent paths", () =>
        {
            Assert(CiPagesPublicationPlan.IsSafeRelativePath("site/docs"), "valid nested source path rejected");
            Assert(!CiPagesPublicationPlan.IsSafeRelativePath("../site"), "parent traversal accepted");
            Assert(!CiPagesPublicationPlan.IsSafeRelativePath("/site"), "absolute path accepted");
            Assert(!CiPagesPublicationPlan.IsSafeRelativePath(@"site\\docs"), "backslash path accepted");
            Assert(!CiPagesPublicationPlan.IsSafeRelativePath("site//docs"), "empty path segment accepted");
        });

        Check("Pages declaration rejects unsupported branch and malformed identifiers", () =>
        {
            var invalid = CiPagesPublicationPlan.Expand(valid with
            {
                SourceBranch = "feature/unsafe",
                ArtifactName = "../artifact",
                PlanId = "Pages Plan"
            });
            Assert(!invalid.IsValid && invalid.Workflow is null &&
                   invalid.Errors.Contains("pages.source-branch-invalid") &&
                   invalid.Errors.Contains("pages.artifact-name-invalid") &&
                   invalid.Errors.Contains("pages.plan-id-invalid"),
                "invalid Pages declaration was not rejected with stable diagnostics");
        });

        Check("Pages plan rejects malformed repository and missing source", () =>
        {
            var invalid = CiPagesPublicationPlan.Expand(valid with { Repository = "not-a-repository", SourcePath = " " });
            Assert(!invalid.IsValid && invalid.Errors.Contains("pages.repository-invalid") &&
                   invalid.Errors.Contains("pages.source-path-invalid"),
                "malformed Pages repository/source was accepted");
        });


        Check("pipeline builder composes Pages declaratively and exposes the expanded plan", () =>
        {
            var definition = new CiPipelineBuilder("TrentBest/TheSingularityWorkshop.CiAi")
                .On(CiPipelineTrigger.Manual)
                .WithPages(valid)
                .Build();
            Assert(definition.PagesPlans.Count == 1 && definition.PagesPlans[0].IsValid,
                "pipeline definition did not retain its Pages plan");
            Assert(definition.PagesPlans[0].PublicationPolicy.Mode == CiPublicationMode.Automatic,
                "pipeline Pages plan lost its automatic destination policy");
        });

        Check("pipeline builder rejects Pages repository mismatch and duplicate Pages destinations", () =>
        {
            var builder = new CiPipelineBuilder("TrentBest/TheSingularityWorkshop.CiAi")
                .On(CiPipelineTrigger.Manual);
            Throws<ArgumentException>(() => builder.WithPages(valid with { Repository = "Other/Repository" }));
            builder.WithPages(valid);
            Throws<InvalidOperationException>(() => builder.WithPages(valid));
        });

        Check("pipeline builder refuses invalid Pages declarations", () =>
        {
            var builder = new CiPipelineBuilder("TrentBest/TheSingularityWorkshop.CiAi")
                .On(CiPipelineTrigger.Manual);
            Throws<ArgumentException>(() => builder.WithPages(valid with { SourcePath = "../escape" }));
        });


        Check("Pages workflow generation is deterministic and pins every external action", () =>
        {
            var pins = new CiPagesActionPins(new string('a', 40), new string('b', 40),
                new string('c', 40), new string('d', 40));
            var first = CiPagesWorkflowGenerator.Generate(valid, pins);
            var second = CiPagesWorkflowGenerator.Generate(valid, pins);
            Assert(first == second, "equivalent Pages inputs generated different workflow text");
            Assert(first.Contains("actions/checkout@" + pins.Checkout, StringComparison.Ordinal) &&
                   first.Contains("actions/configure-pages@" + pins.ConfigurePages, StringComparison.Ordinal) &&
                   first.Contains("actions/upload-pages-artifact@" + pins.UploadPagesArtifact, StringComparison.Ordinal) &&
                   first.Contains("actions/deploy-pages@" + pins.DeployPages, StringComparison.Ordinal),
                "generated workflow omitted immutable action pins");
        });

        Check("Pages workflow serializes deployments and uses least-privilege permissions", () =>
        {
            var pins = new CiPagesActionPins(new string('a', 40), new string('b', 40),
                new string('c', 40), new string('d', 40));
            var workflow = CiPagesWorkflowGenerator.Generate(valid, pins);
            Assert(workflow.Contains("cancel-in-progress: false", StringComparison.Ordinal),
                "Pages deployment concurrency can cancel a publication in flight");
            Assert(workflow.Contains("pages: write", StringComparison.Ordinal) &&
                   workflow.Contains("id-token: write", StringComparison.Ordinal) &&
                   workflow.Contains("contents: read", StringComparison.Ordinal),
                "Pages workflow permissions are incomplete");
            Assert(workflow.Contains("test -f \\"site/index.html\\"", StringComparison.Ordinal),
                "Pages workflow does not validate its entry point");
            Assert(workflow.Contains("workflow_dispatch:", StringComparison.Ordinal),
                "manual diagnostic invocation is missing");
        });

        Check("Pages workflow generator rejects mutable or malformed action references", () =>
        {
            var pins = new CiPagesActionPins("v4", new string('b', 40),
                new string('c', 40), new string('d', 40));
            Throws<ArgumentException>(() => CiPagesWorkflowGenerator.Generate(valid, pins));
        });

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine("PASS " + name);
        }

        static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static void Throws<TException>(Action action) where TException : Exception
        {
            try { action(); }
            catch (TException) { return; }
            throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
        }
    }
}
