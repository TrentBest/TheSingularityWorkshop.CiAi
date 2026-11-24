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
    }
}
