using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Renders the observer's raw commit-observation JSON as deterministic Markdown.
/// This is an observation summary, not a release-gate verdict.
/// </summary>
public static class CommitObservationMarkdownRenderer
{
    public static string Render(string observationJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(observationJson);
        using var document = JsonDocument.Parse(observationJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            RequiredString(root, "schema") != "ciai.commit-observation.v1")
            throw new InvalidDataException("Input is not a supported CiAi commit-observation document.");

        var repository = RequiredString(root, "repository");
        var headSha = RequiredString(root, "headSha");
        if (!RepositoryIdentity.IsValid(repository))
            throw new InvalidDataException("Observation contains a non-canonical repository identity.");
        if (headSha.Length != 40 || !headSha.All(Uri.IsHexDigit))
            throw new InvalidDataException("Observation must identify one full commit SHA.");

        var complete = root.TryGetProperty("complete", out var completeValue) &&
            completeValue.ValueKind == JsonValueKind.True;
        var lines = new List<string>
        {
            "# CiAi commit observation",
            "",
            $"- **Repository:** `{Escape(repository)}`",
            $"- **Candidate commit:** `{headSha.ToLowerInvariant()}`",
            $"- **Observation complete:** {(complete ? "yes" : "no")}",
            $"- **Retrieved (UTC):** {Escape(RequiredString(root, "retrievedAtUtc"))}",
            "",
            "> Read-only workflow observation only. This document does not certify release readiness or authorize publication.",
            "",
            "## Discovery",
            "",
            "| Metric | Value |",
            "| --- | ---: |"
        };

        if (!root.TryGetProperty("discovery", out var discovery) || discovery.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Observation is missing discovery metadata.");
        foreach (var name in new[] { "PagesRead", "TotalCount", "IsTruncated", "returnedRuns" })
        {
            if (!discovery.TryGetProperty(name, out var value))
                throw new InvalidDataException($"Observation discovery metadata is missing '{name}'.");
            lines.Add($"| {name} | {Escape(value.ToString())} |");
        }

        lines.Add("");
        lines.Add("## Required workflow policy");
        lines.Add("");
        if (root.TryGetProperty("requiredWorkflowPolicy", out var policy) && policy.ValueKind == JsonValueKind.Object)
        {
            var configured = policy.TryGetProperty("configured", out var configuredValue) &&
                configuredValue.ValueKind == JsonValueKind.True;
            lines.Add($"- **Configured:** {(configured ? "yes" : "no")}");
            if (policy.TryGetProperty("workflowIdentities", out var paths) && paths.ValueKind == JsonValueKind.Array)
                foreach (var path in paths.EnumerateArray())
                    if (path.ValueKind == JsonValueKind.String)
                        lines.Add($"- Required workflow: `{Escape(path.GetString() ?? string.Empty)}`");
        }
        else
        {
            lines.Add("Required workflow policy was not included in this report.");
        }
        foreach (var field in new[] { "discoveryComplete", "requiredEvidenceComplete", "requiredWorkflowsTerminal", "requiredWorkflowsSuccessful", "complete" })
        {
            var value = root.TryGetProperty(field, out var fieldValue) && fieldValue.ValueKind == JsonValueKind.True;
            lines.Add($"- **{field}:** {(value ? "yes" : "no")}");
        }
        if (root.TryGetProperty("requiredWorkflowResults", out var policyResults) && policyResults.ValueKind == JsonValueKind.Array)
        {
            lines.Add("");
            lines.Add("| Required workflow identity | State | Evidence complete | Terminal | Successful | Run IDs | Reason |");
            lines.Add("| --- | --- | --- | --- | --- | --- | --- |");
            foreach (var result in policyResults.EnumerateArray())
            {
                var workflowPath = RequiredString(result, "WorkflowPath");
                var state = RequiredString(result, "State");
                var runIds = result.TryGetProperty("MatchingRunIds", out var ids) && ids.ValueKind == JsonValueKind.Array
                    ? string.Join(", ", ids.EnumerateArray().Select(id => id.ToString())) : "—";
                var reason = RequiredString(result, "Reason");
                var evidenceComplete = result.TryGetProperty("EvidenceComplete", out var evidenceValue) && evidenceValue.ValueKind == JsonValueKind.True;
                var terminal = result.TryGetProperty("Terminal", out var terminalValue) && terminalValue.ValueKind == JsonValueKind.True;
                var successful = result.TryGetProperty("Successful", out var successValue) && successValue.ValueKind == JsonValueKind.True;
                lines.Add($"| `{Escape(workflowPath)}` | {Escape(state)} | {(evidenceComplete ? "yes" : "no")} | {(terminal ? "yes" : "no")} | {(successful ? "yes" : "no")} | {Escape(runIds)} | {Escape(reason)} |");
            }
        }

        lines.Add("");
        lines.Add("## Workflow runs and jobs");
        lines.Add("");
        if (!root.TryGetProperty("runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Observation is missing its runs array.");
        if (runs.GetArrayLength() == 0)
        {
            lines.Add("No matching workflow runs were observed.");
        }
        else
        {
            foreach (var item in runs.EnumerateArray()
                .OrderBy(x => x.GetProperty("run").GetProperty("WorkflowName").GetString(), StringComparer.Ordinal)
                .ThenBy(x => x.GetProperty("run").GetProperty("RunId").GetInt64()))
            {
                var run = item.GetProperty("run");
                lines.Add($"### {Escape(RequiredString(run, "WorkflowName"))}");
                lines.Add("");
                lines.Add($"- Run: [{run.GetProperty("RunId").GetInt64()}]({EscapeUrl(RequiredString(run, "evidenceUrl"))})");
                lines.Add($"- Attempt: {run.GetProperty("RunAttempt").GetInt32()}");
                lines.Add($"- Event: {Escape(RequiredString(run, "Event"))}");
                lines.Add($"- Status / conclusion: {Escape(RequiredString(run, "normalizedStatus"))} / {Escape(RequiredString(run, "normalizedConclusion"))}");
                lines.Add($"- Workflow path: `{Escape(RequiredString(run, "WorkflowPath"))}`");
                lines.Add("");
                lines.Add("| Job | Status | Conclusion | Steps |");
                lines.Add("| --- | --- | --- | ---: |");
                if (!item.TryGetProperty("jobs", out var jobs) || jobs.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Observed workflow run is missing its jobs array.");
                foreach (var job in jobs.EnumerateArray()
                    .OrderBy(x => x.GetProperty("Name").GetString(), StringComparer.Ordinal)
                    .ThenBy(x => x.GetProperty("JobId").GetInt64()))
                {
                    var conclusion = job.TryGetProperty("Conclusion", out var conclusionValue) &&
                        conclusionValue.ValueKind == JsonValueKind.String ? conclusionValue.GetString() ?? "—" : "—";
                    var steps = job.TryGetProperty("steps", out var stepArray) && stepArray.ValueKind == JsonValueKind.Array
                        ? stepArray.GetArrayLength().ToString(System.Globalization.CultureInfo.InvariantCulture) : "0";
                    lines.Add($"| [{Escape(RequiredString(job, "Name"))}]({EscapeUrl(RequiredString(job, "evidenceUrl"))}) | " +
                        $"{Escape(RequiredString(job, "Status"))} | {Escape(conclusion)} | {steps} |");
                }
                var jobsComplete = item.TryGetProperty("jobsComplete", out var jobsCompleteValue) &&
                    jobsCompleteValue.ValueKind == JsonValueKind.True;
                lines.Add("");
                lines.Add($"Jobs collection complete: **{(jobsComplete ? "yes" : "no")}**.");
                lines.Add("");
            }
        }

        lines.Add("## Interpretation limits");
        lines.Add("");
        lines.Add("- Raw workflow and job states are shown as observed; no release category is inferred from a workflow name.");
        lines.Add("- Incomplete discovery, missing jobs, skipped jobs, or absent evidence must not be treated as passing.");
        lines.Add("- Package contents, public consumer compatibility, branch reconciliation, and owner authorization require their own evidence.");
        lines.Add("");
        return string.Join("\n", lines);
    }

    private static string RequiredString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"Observation is missing required string '{property}'.");
        return value.GetString()!;
    }

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("`", "\\`", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal);

    private static string EscapeUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.IsDefaultPort || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidDataException("Observation contains an unsafe or invalid GitHub evidence URL.");
        return uri.AbsoluteUri;
    }
}
