namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Validates the public observer's exact provider run binding. Selectors bind workflow path,
/// run ID, and attempt; provider-observed workflow ref and event are independent required facts.
/// </summary>
public static class CiExactWorkflowRunBindingEvaluator
{
    public static bool IsValid(
        IReadOnlyCollection<string> selectors,
        IReadOnlyCollection<CiWorkflowRunObservation> observations,
        string expectedRepository,
        string expectedHeadSha,
        string? expectedWorkflowRef,
        string? expectedEvent)
    {
        ArgumentNullException.ThrowIfNull(selectors);
        ArgumentNullException.ThrowIfNull(observations);

        if (selectors.Count == 0 ||
            !RepositoryIdentity.IsValid(expectedRepository) ||
            expectedHeadSha.Length != 40 ||
            !expectedHeadSha.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(expectedWorkflowRef) ||
            string.IsNullOrWhiteSpace(expectedEvent) ||
            selectors.Distinct(StringComparer.Ordinal).Count() != selectors.Count)
            return false;

        foreach (var selector in selectors)
        {
            if (string.IsNullOrWhiteSpace(selector))
                return false;

            var parts = selector.Split('#');
            if (parts.Length != 3 ||
                !long.TryParse(parts[1], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var expectedRunId) ||
                expectedRunId <= 0 ||
                !int.TryParse(parts[2], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var expectedAttempt) ||
                expectedAttempt <= 0)
                return false;

            var matches = observations.Where(observation =>
                observation.Run.RunId == expectedRunId &&
                observation.RunAttempt == expectedAttempt &&
                string.Equals(observation.WorkflowPath, parts[0], StringComparison.Ordinal)).ToArray();

            if (matches.Length != 1)
                return false;

            var match = matches[0];
            if (!string.Equals(match.Run.Repository, expectedRepository, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(match.Run.HeadSha, expectedHeadSha, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(match.WorkflowRef, expectedWorkflowRef, StringComparison.Ordinal) ||
                !string.Equals(match.Event, expectedEvent, StringComparison.Ordinal) ||
                match.Run.Status != CiRunStatus.Completed ||
                !string.Equals(match.ProviderStatus, "completed", StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
