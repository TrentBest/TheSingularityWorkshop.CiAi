namespace TheSingularityWorkshop.CiAi;

public sealed record CiPagesPublicationPlanDraft(
    string Repository,
    string SourcePath,
    string SourceBranch,
    string ArtifactName = "pages-site",
    string PlanId = "pages-publication",
    string BuildCommand = "",
    string OutputPath = ".");

public sealed record CiPagesPublicationPlanExpansion(
    bool IsValid,
    IReadOnlyList<string> Errors,
    CiWorkflowPlanDraft? Workflow,
    CiPublicationPolicy PublicationPolicy);

public static class CiPagesPublicationPlan
{
    public static CiPagesPublicationPlanExpansion Expand(CiPagesPublicationPlanDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(draft.Repository) || !RepositoryIdentity.IsValid(draft.Repository))
            errors.Add("pages.repository-invalid");
        if (!IsSafeRelativePath(draft.SourcePath))
            errors.Add("pages.source-path-invalid");
        if (draft.SourceBranch is not ("development" or "master"))
            errors.Add("pages.source-branch-invalid");
        if (!IsStableId(draft.ArtifactName))
            errors.Add("pages.artifact-name-invalid");
        if (!IsStableId(draft.PlanId))
            errors.Add("pages.plan-id-invalid");
        if (!IsSafeBuildCommand(draft.BuildCommand))
            errors.Add("pages.build-command-invalid");
        if (draft.OutputPath != "." && !IsSafeRelativePath(draft.OutputPath))
            errors.Add("pages.output-path-invalid");

        var policy = new CiPublicationPolicy("github-pages", CiPublicationMode.Automatic, CiPublicationApproval.None);
        if (errors.Count > 0)
            return new CiPagesPublicationPlanExpansion(false, Array.AsReadOnly(errors.ToArray()), null, policy);

        var workflow = new CiWorkflowPlanDraft(
            draft.Repository,
            draft.PlanId,
            Array.AsReadOnly(new[]
            {
                Step("build-site", "Build or stage Pages source using the declared repository command", "build-test"),
                Step("validate-site", "Validate generated Pages site entry point", "documentation", "build-site"),
                Step("package-site", "Package exact validated Pages artifact", "artifact-handoff", "validate-site"),
                Step("deploy-pages", "Deploy validated artifact to GitHub Pages", "deployment", "package-site")
            }),
            Array.AsReadOnly(new[] { $"push:{draft.SourceBranch}", "workflow_dispatch" }));

        var validation = CiWorkflowPlanValidator.Validate(workflow);
        if (!validation.IsValid)
            return new CiPagesPublicationPlanExpansion(false, validation.Errors, null, policy);

        return new CiPagesPublicationPlanExpansion(true, Array.Empty<string>(), workflow, policy);
    }

    public static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 240 || path.StartsWith('/') ||
            path.Contains('\\') || path.Any(char.IsControl))
            return false;

        var segments = path.Split('/');
        return segments.All(segment =>
            segment.Length > 0 &&
            segment != "." &&
            segment != ".." &&
            segment.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'));
    }

    public static bool IsSafeBuildCommand(string? command) =>
        string.IsNullOrEmpty(command) ||
        (!string.IsNullOrWhiteSpace(command) && command.Length <= 1000 && !command.Any(char.IsControl));

    private static bool IsStableId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 80 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static CiWorkflowPlanStepDraft Step(
        string id, string name, string domain, params string[] dependencies) =>
        new(id, name, domain, Array.AsReadOnly(dependencies),
            CiWorkflowStepCondition.AllDependenciesSucceeded);
}
