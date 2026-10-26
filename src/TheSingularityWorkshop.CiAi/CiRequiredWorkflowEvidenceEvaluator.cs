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
    string Reason);

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
        if (required.Length == 0 || required.Any(path => path.Length == 0 || !path.StartsWith(".github/workflows/", StringComparison.Ordinal)))
            throw new ArgumentException("At least one exact workflow path under .github/workflows/ is required.", nameof(requiredWorkflowPaths));
        if (required.Distinct(StringComparer.Ordinal).Count() != required.Length)
            throw new ArgumentException("Required workflow paths must be unique.", nameof(requiredWorkflowPaths));

        var runs = observedRuns.ToArray();
        return Array.AsReadOnly(required.Select(path =>
        {
            var matching = runs.Where(run => string.Equals(run.WorkflowPath, path, StringComparison.Ordinal)).ToArray();
            if (matching.Length == 0)
                return new CiRequiredWorkflowResult(path, "missing", Array.Empty<long>(), "No matching run was observed for this required workflow.");
            if (matching.Length > 1)
                return new CiRequiredWorkflowResult(path, "ambiguous", Array.AsReadOnly(matching.Select(run => run.RunId).Distinct().Order().ToArray()),
                    "Multiple runs match this required workflow path; policy requires an unambiguous run identity.");
            var run = matching[0];
            var ids = Array.AsReadOnly(new[] { run.RunId });
            if (run.RunAttempt <= 0)
                return new CiRequiredWorkflowResult(path, "incomplete", ids, "Run attempt identity is invalid.");
            if (run.Status != CiRunStatus.Completed)
                return new CiRequiredWorkflowResult(path, "incomplete", ids, "Required workflow has not reached a recognized terminal status.");
            if (run.Conclusion != CiRunConclusion.Success)
                return new CiRequiredWorkflowResult(path, "failed", ids, "Required workflow did not conclude with success.");
            if (!run.JobsComplete)
                return new CiRequiredWorkflowResult(path, "incomplete", ids, "Required workflow job evidence is missing or truncated.");
            return new CiRequiredWorkflowResult(path, "passed", ids, "Required workflow completed successfully with complete job evidence.");
        }).ToArray());
    }

    public static bool IsPassing(
        bool discoveryComplete,
        IEnumerable<CiRequiredWorkflowResult> results) =>
        discoveryComplete && results is not null &&
        (results.Any()) && results.All(result => result.State == "passed");
}
