namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Reconciles a provider-fetched run with coordinator-owned expectations.
/// This is the trust boundary between a valid GitHub snapshot and a verified operation result.
/// </summary>
public static class CiRunEvidenceVerifier
{
    public static IReadOnlyList<string> VerifySuccessfulRun(
        CiRunSnapshot? snapshot,
        string expectedRepository,
        long expectedRunId,
        string expectedHeadSha)
    {
        var errors = new List<string>();
        if (!RepositoryIdentity.IsValid(expectedRepository))
            errors.Add("expected.repository.invalid");
        if (expectedRunId <= 0)
            errors.Add("expected.runId.invalid");
        if (!IsCommitSha(expectedHeadSha))
            errors.Add("expected.headSha.invalid");
        if (snapshot is null)
            return errors.Append("snapshot.required").ToArray();

        if (!string.Equals(snapshot.Repository, expectedRepository, StringComparison.OrdinalIgnoreCase))
            errors.Add("repository.mismatch");
        if (snapshot.RunId != expectedRunId)
            errors.Add("runId.mismatch");
        if (!string.Equals(snapshot.HeadSha, expectedHeadSha, StringComparison.OrdinalIgnoreCase))
            errors.Add("headSha.mismatch");
        if (snapshot.Status != CiRunStatus.Completed)
            errors.Add("run.not-completed");
        if (snapshot.Conclusion != CiRunConclusion.Success)
            errors.Add("run.not-successful");
        return errors;
    }

    private static bool IsCommitSha(string? value) =>
        value is { Length: 40 } && value.All(Uri.IsHexDigit);
}
