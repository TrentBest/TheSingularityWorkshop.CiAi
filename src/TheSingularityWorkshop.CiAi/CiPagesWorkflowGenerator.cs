namespace TheSingularityWorkshop.CiAi;

public sealed record CiPagesActionPins(string Checkout, string ConfigurePages, string UploadPagesArtifact, string DeployPages)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        ValidatePin(Checkout, "checkout", errors);
        ValidatePin(ConfigurePages, "configure-pages", errors);
        ValidatePin(UploadPagesArtifact, "upload-pages-artifact", errors);
        ValidatePin(DeployPages, "deploy-pages", errors);
        return Array.AsReadOnly(errors.ToArray());
    }

    private static void ValidatePin(string? pin, string id, ICollection<string> errors)
    {
        if (pin is null || pin.Length != 40 || !pin.All(Uri.IsHexDigit))
            errors.Add($"pages.action-pin-invalid:{id}");
    }
}

public static class CiPagesWorkflowGenerator
{
    public const string WorkflowPath = ".github/workflows/pages.yml";

    public static string Generate(CiPagesPublicationPlanDraft draft, CiPagesActionPins pins)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(pins);
        var expansion = CiPagesPublicationPlan.Expand(draft);
        if (!expansion.IsValid)
            throw new ArgumentException("Pages declaration is invalid: " + string.Join(", ", expansion.Errors), nameof(draft));
        var pinErrors = pins.Validate();
        if (pinErrors.Count > 0)
            throw new ArgumentException("Pages action pins are invalid: " + string.Join(", ", pinErrors), nameof(pins));

        var source = draft.SourcePath;
        var branch = draft.SourceBranch;
        var lines = new[]
        {
            "name: Deploy Pages",
            "",
            "on:",
            "  push:",
            "    branches:",
            $"      - \"{branch}\"",
            "    paths:",
            $"      - \"{source}/**\"",
            $"      - \"{WorkflowPath}\"",
            "  workflow_dispatch:",
            "",
            "permissions:",
            "  contents: read",
            "",
            "concurrency:",
            "  group: pages-${{ github.repository }}",
            "  cancel-in-progress: false",
            "",
            "jobs:",
            "  deploy:",
            "    permissions:",
            "      contents: read",
            "      pages: write",
            "      id-token: write",
            "    environment:",
            "      name: github-pages",
            "      url: ${{ steps.deployment.outputs.page_url }}",
            "    runs-on: ubuntu-latest",
            "    steps:",
            "      - name: Checkout exact source revision",
            $"        uses: actions/checkout@{pins.Checkout}",
            "      - name: Configure Pages",
            $"        uses: actions/configure-pages@{pins.ConfigurePages}",
            "      - name: Validate site entry point",
            "        shell: bash",
            "        run: |",
            "          set -euo pipefail",
            $"          test -d \"{source}\"",
            $"          test -f \"{source}/index.html\"",
            "      - name: Upload validated Pages artifact",
            $"        uses: actions/upload-pages-artifact@{pins.UploadPagesArtifact}",
            "        with:",
            $"          path: \"{source}\"",
            "      - name: Deploy the exact uploaded artifact",
            "        id: deployment",
            $"        uses: actions/deploy-pages@{pins.DeployPages}",
            ""
        };
        return string.Join(Environment.NewLine, lines);
    }
}
