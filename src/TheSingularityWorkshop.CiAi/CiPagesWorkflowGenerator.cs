namespace TheSingularityWorkshop.CiAi;

public sealed record CiPagesActionPins(
    string Checkout,
    string UploadArtifact,
    string ConfigurePages,
    string DownloadArtifact,
    string UploadPagesArtifact,
    string DeployPages)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        ValidatePin(Checkout, "checkout", errors);
        ValidatePin(UploadArtifact, "upload-artifact", errors);
        ValidatePin(ConfigurePages, "configure-pages", errors);
        ValidatePin(DownloadArtifact, "download-artifact", errors);
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
        var output = draft.OutputPath;
        var artifact = draft.ArtifactName;
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
            "  build:",
            "    runs-on: ubuntu-24.04",
            "    permissions:",
            "      contents: read",
            "    steps:",
            "      - name: Checkout exact source revision",
            $"        uses: actions/checkout@{pins.Checkout} # v4",
            "        with:",
            "          persist-credentials: false",
            "      - name: Build site",
            $"        working-directory: \"{source}\"",
            "        shell: bash",
            "        run: |",
            "          set -euo pipefail",
            $"          {draft.BuildCommand}",
            "      - name: Validate generated site entry point",
            $"        working-directory: \"{source}\"",
            "        shell: bash",
            "        run: |",
            "          set -euo pipefail",
            $"          test -f \"{output}/index.html\"",
            "      - name: Upload exact built-site artifact",
            $"        uses: actions/upload-artifact@{pins.UploadArtifact} # v4",
            "        with:",
            $"          name: \"{artifact}\"",
            $"          path: \"{source}/{output}\"",
            "          if-no-files-found: error",
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
            "      url: ${{ steps.deployment.outputs.page_url }}",
            "    steps:",
            "      - name: Configure Pages",
            $"        uses: actions/configure-pages@{pins.ConfigurePages} # v5",
            "      - name: Download exact built-site artifact",
            $"        uses: actions/download-artifact@{pins.DownloadArtifact} # v4",
            "        with:",
            $"          name: \"{artifact}\"",
            "          path: _site",
            "      - name: Upload Pages deployment artifact",
            $"        uses: actions/upload-pages-artifact@{pins.UploadPagesArtifact} # v3",
            "        with:",
            "          path: _site",
            "      - name: Deploy the exact uploaded artifact",
            "        id: deployment",
            $"        uses: actions/deploy-pages@{pins.DeployPages} # v4",
            ""
        };
        return string.Join("\n", lines);
    }
}
