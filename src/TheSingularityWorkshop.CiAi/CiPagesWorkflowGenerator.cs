namespace TheSingularityWorkshop.CiAi;

public sealed record CiPagesActionPins(
    string Checkout,
    string ConfigurePages,
    string UploadPagesArtifact,
    string DeployPages)
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

        var outputPath = draft.OutputPath == "." ? $"{draft.SourcePath}/index.html" : $"{draft.SourcePath}/{draft.OutputPath}/index.html";
        var artifactPath = draft.OutputPath == "." ? draft.SourcePath : $"{draft.SourcePath}/{draft.OutputPath}";
        var lines = new List<string>
        {
            "name: Deploy Pages",
            "",
            "on:",
            "  push:",
            "    branches:",
            $"      - \"{draft.SourceBranch}\"",
            "  workflow_dispatch:",
            "",
            "permissions:",
            "  contents: read",
            "",
            "concurrency:",
            "  group: pages-" + "$" + "{{ github.repository }}",
            "  cancel-in-progress: false",
            "",
            "jobs:",
            "  build:",
            "    runs-on: ubuntu-24.04",
            "    permissions:",
            "      contents: read",
            "    steps:",
            "      - name: Checkout exact source revision",
            $"        uses: actions/checkout@{pins.Checkout} # v4",
            "        with:",
            "          persist-credentials: false"
        };

        if (!string.IsNullOrWhiteSpace(draft.BuildCommand))
        {
            lines.AddRange(new[]
            {
                "      - name: Build site",
                "        shell: bash",
                "        run: |",
                "          set -euo pipefail",
                $"          {draft.BuildCommand}"
            });
        }

        lines.AddRange(new[]
        {
            "      - name: Validate generated site entry point",
            "        shell: bash",
            "        run: |",
            "          set -euo pipefail",
            $"          test -f \"{outputPath}\"",
            "      - name: Upload exact built-site artifact",
            $"        uses: actions/upload-pages-artifact@{pins.UploadPagesArtifact} # v3",
            "        with:",
            $"          name: \"{draft.ArtifactName}\"",
            $"          path: \"{artifactPath}\"",
            "          retention-days: 1",
            "",
            "  deploy:",
            "    needs: build",
            "    runs-on: ubuntu-24.04",
            "    permissions:",
            "      contents: read",
            "      pages: write",
            "      id-token: write",
            "    environment:",
            "      name: github-pages",
            "      url: " + "$" + "{{ steps.deployment.outputs.page_url }}",
            "    steps:",
            "      - name: Configure Pages",
            $"        uses: actions/configure-pages@{pins.ConfigurePages} # v5",
            "      - name: Deploy the exact uploaded artifact",
            "        id: deployment",
            $"        uses: actions/deploy-pages@{pins.DeployPages} # v4",
            "        with:",
            $"          artifact_name: \"{draft.ArtifactName}\"",
            ""
        });
        return string.Join("\n", lines);
    }
}
