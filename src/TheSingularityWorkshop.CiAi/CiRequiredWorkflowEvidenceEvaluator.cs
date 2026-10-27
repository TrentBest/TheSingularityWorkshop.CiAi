namespace TheSingularityWorkshop.CiAi;

/// <summary>One workflow's observed evidence for a commit-scoped required-workflow policy.</summary>
public sealed record CiRequiredWorkflowRunEvidence(
    string WorkflowPath,
    long RunId,
    int RunAttempt,
    CiRunStatus Status,
    CiRunConclusion Conclusion,
    bool JobsComplete);

/// <summary>The result for one explicitly required workflow identity.</summary>
public sealed record CiRequiredWorkflowResult(
    string WorkflowPath,
    string State,
    IReadOnlyList<long> MatchingRunIds,
    string Reason,
    bool EvidenceComplete,
    bool Terminal,
    bool Successful);

/// <summary>
/// Evaluates only explicitly required workflow paths. Unrelated runs remain diagnostic evidence
/// but cannot block the policy. Missing, duplicate, incomplete, non-terminal, or non-successful
/// required evidence never passes.
/// </summary>
public static class CiRequiredWorkflowEvidenceEvaluator
{
    public static IReadOnlyList<CiRequiredWorkflowResult> Evaluate(
        IEnumerable<string> requiredWorkflowPaths,
        IEnumerable<CiRequiredWorkflowRunEvidence> observedRuns)
    {
        ArgumentNullException.ThrowIfNull(requiredWorkflowPaths);
        ArgumentNullException.ThrowIfNull(observedRuns);

        var required = requiredWorkflowPaths
            .Select(path => path?.Trim() ?? string.Empty)
            .ToArray();
        if (required.Length == 0 || required.Any(selector =>
        {
            var parts = selector.Split('#');
            var explicitMissing = parts.Length == 2 && parts[1] == "missing";
            return parts.Length is < 1 or > 3 ||
                !IsValidWorkflowPath(parts[0]) ||
                (parts.Length >= 2 && !explicitMissing && (!long.TryParse(parts[1], out var runId) || runId <= 0)) ||
                (parts.Length == 3 && (!int.TryParse(parts[2], out var attempt) || attempt <= 0)) ||
                (explicitMissing && parts.Length != 2);
        }))
            throw new ArgumentException("At least one valid workflow identity is required. Use a canonical .github/workflows/ path ending in .yml or .yaml, optionally suffixed with #<positive-run-id>[#<positive-attempt>].", nameof(requiredWorkflowPaths));
        if (required.Distinct(StringComparer.Ordinal).Count() != required.Length)
            throw new ArgumentException("Required workflow identities must be unique.", nameof(requiredWorkflowPaths));

        var runs = observedRuns.ToArray();
        return Array.AsReadOnly(required.Select(selector =>
        {
            var parts = selector.Split('#');
            var path = parts[0];
            var explicitMissing = parts.Length == 2 && parts[1] == "missing";
            long? expectedRunId = parts.Length >= 2 && !explicitMissing
                ? long.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : null;
            int? expectedAttempt = parts.Length == 3
                ? int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) : null;
            var matching = explicitMissing ? Array.Empty<CiRequiredWorkflowRunEvidence>() : runs.Where(run =>
                string.Equals(run.WorkflowPath, path, StringComparison.Ordinal) &&
                (!expectedRunId.HasValue || run.RunId == expectedRunId.Value) &&
                (!expectedAttempt.HasValue || run.RunAttempt == expectedAttempt.Value)).ToArray();
            if (matching.Length == 0)
                return new CiRequiredWorkflowResult(selector, "missing", Array.Empty<long>(), "No matching run was observed for this required workflow.", false, false, false);
            if (matching.Length > 1)
                return new CiRequiredWorkflowResult(selector, "ambiguous", Array.AsReadOnly(matching.Select(run => run.RunId).Distinct().Order().ToArray()),
                    "Multiple runs match this required workflow identity; policy requires an unambiguous run identity.", false, false, false);
            var run = matching[0];
            var ids = Array.AsReadOnly(new[] { run.RunId });
            if (run.RunId <= 0 || run.RunAttempt <= 0)
                return new CiRequiredWorkflowResult(selector, "incomplete", ids, "Run and attempt identities must be positive.", false, false, false);
            if (run.Status != CiRunStatus.Completed)
                return new CiRequiredWorkflowResult(selector, "incomplete", ids, "Required workflow has not reached a recognized terminal status.", false, false, false);
            if (run.Conclusion != CiRunConclusion.Success)
                return new CiRequiredWorkflowResult(selector, "failed", ids, "Required workflow did not conclude with success.", run.JobsComplete, true, false);
            if (!run.JobsComplete)
                return new CiRequiredWorkflowResult(selector, "incomplete", ids, "Required workflow job evidence is missing or truncated.", false, true, true);
            return new CiRequiredWorkflowResult(selector, "passed", ids, "Required workflow completed successfully with complete job evidence.", true, true, true);
        }).ToArray());
    }

    public static bool IsPassing(
        bool discoveryComplete,
        IEnumerable<CiRequiredWorkflowResult> results)
    {
        if (!discoveryComplete || results is null) return false;
        var materialized = results.ToArray();
        return materialized.Length > 0 &&
            materialized.All(result => result is not null &&
                result.EvidenceComplete && result.Terminal && result.Successful);
    }

    private static bool IsValidWorkflowPath(string path)
    {
        if (!path.StartsWith(".github/workflows/", StringComparison.Ordinal) ||
            !(path.EndsWith(".yml", StringComparison.Ordinal) || path.EndsWith(".yaml", StringComparison.Ordinal)) ||
            path.Contains('\\') || path.Contains('#') || path.Contains('?') || path.Contains(':'))
            return false;

        var segments = path.Split('/');
        return segments.Length >= 3 &&
            segments.All(segment => segment.Length > 0 && segment != "." && segment != ".." &&
                segment.All(character => char.IsAsciiLetterOrDigit(character) ||
                    character is '-' or '_' or '.'));
    }
}
