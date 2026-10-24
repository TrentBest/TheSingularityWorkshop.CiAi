namespace TheSingularityWorkshop.CiAi;

/// <summary>Renders a deterministic, human-readable Markdown view of exact-SHA release evidence.</summary>
public static class ReleaseEvidenceMarkdownRenderer
{
    /// <summary>
    /// Renders all aggregated categories in enum order. The renderer is presentation-only:
    /// it never upgrades missing, skipped, unverified, or failed evidence to a pass.
    /// </summary>
    public static string Render(ReleaseEvidenceReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var lines = new List<string>
        {
            "# Release evidence report",
            "",
            $"- **Repository:** `{EscapeInline(report.Repository)}`",
            $"- **Candidate commit:** `{EscapeInline(report.HeadSha)}`",
            $"- **Overall evidence status:** {(report.IsPassing ? "PASS (required categories only)" : "NOT PASSING")}",
            "",
            "> This report summarizes supplied evidence only. A passing evidence summary is not release authorization.",
            "",
            "## Gate summary",
            "",
            "| Category | State |",
            "| --- | --- |"
        };

        foreach (var category in report.Categories.OrderBy(x => x.Key))
            lines.Add($"| {category.Key} | **{category.Value}** |");

        lines.Add("");
        lines.Add("## Evidence records");
        lines.Add("");
        if (report.Records.Count == 0)
        {
            lines.Add("No evidence records were supplied.");
        }
        else
        {
            lines.Add("| Category | State | Raw status | Raw conclusion | Retrieved (UTC) | Evidence | Diagnostic |");
            lines.Add("| --- | --- | --- | --- | --- | --- | --- |");
            foreach (var record in report.Records
                .OrderBy(x => x.Category)
                .ThenBy(x => x.RetrievedAt)
                .ThenBy(x => x.RunId ?? 0)
                .ThenBy(x => x.JobIdentity, StringComparer.Ordinal))
            {
                var evidence = record.EvidenceUrl is null
                    ? "—"
                    : $"[open]({record.EvidenceUrl.AbsoluteUri})";
                lines.Add($"| {record.Category} | {record.State} | {EscapeCell(record.RawStatus)} | " +
                    $"{EscapeCell(record.RawConclusion)} | {record.RetrievedAt.ToUniversalTime():O} | " +
                    $"{evidence} | {EscapeCell(record.Diagnostic ?? "—")} |");
            }
        }

        lines.Add("");
        lines.Add("## Release controls");
        lines.Add("");
        lines.Add("- Publication, deployment, branch reconciliation, and owner authorization are independent gates.");
        lines.Add("- Missing evidence is not a pass; skipped work remains skipped.");
        lines.Add("- Artifact digests are reported only when directly supplied and scoped to the verified bytes.");
        lines.Add("");
        return string.Join("\n", lines);
    }

    private static string EscapeInline(string value) =>
        value.Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static string EscapeCell(string value) =>
        EscapeInline(value).Replace("|", "\\|", StringComparison.Ordinal);
}
