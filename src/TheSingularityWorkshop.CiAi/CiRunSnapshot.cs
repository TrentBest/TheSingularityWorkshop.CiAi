namespace TheSingularityWorkshop.CiAi;

/// <summary>Provider-independent, read-only facts about one CI workflow run.</summary>
public sealed record CiRunSnapshot
{
    public CiRunSnapshot(
        string repository,
        long runId,
        string workflowName,
        string headSha,
        CiRunStatus status,
        CiRunConclusion conclusion,
        Uri evidenceUrl,
        DateTimeOffset updatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);
        ArgumentException.ThrowIfNullOrWhiteSpace(headSha);
        ArgumentNullException.ThrowIfNull(evidenceUrl);

        var repositoryParts = repository.Split('/');
        if (repositoryParts.Length != 2 || repositoryParts.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Repository must use the owner/repository form.", nameof(repository));
        if (runId <= 0)
            throw new ArgumentOutOfRangeException(nameof(runId), "Run ID must be positive.");
        if (headSha.Length < 7 || headSha.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Head SHA must be a hexadecimal commit identifier.", nameof(headSha));
        var expectedEvidencePath = $"/{repository}/actions/runs/{runId}";
        if (evidenceUrl.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(evidenceUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(evidenceUrl.AbsolutePath, expectedEvidencePath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Evidence URL must identify this exact GitHub Actions run over HTTPS.", nameof(evidenceUrl));
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (!Enum.IsDefined(conclusion))
            throw new ArgumentOutOfRangeException(nameof(conclusion));
        if (status != CiRunStatus.Completed && conclusion != CiRunConclusion.None)
            throw new ArgumentException("A non-completed run cannot have a final conclusion.", nameof(conclusion));
        if (status == CiRunStatus.Completed && conclusion == CiRunConclusion.None)
            throw new ArgumentException("A completed run must have a conclusion.", nameof(conclusion));

        Repository = repository;
        RunId = runId;
        WorkflowName = workflowName;
        HeadSha = headSha;
        Status = status;
        Conclusion = conclusion;
        EvidenceUrl = evidenceUrl;
        UpdatedAt = updatedAt;
    }

    public string Repository { get; }
    public long RunId { get; }
    public string WorkflowName { get; }
    public string HeadSha { get; }
    public CiRunStatus Status { get; }
    public CiRunConclusion Conclusion { get; }
    public Uri EvidenceUrl { get; }
    public DateTimeOffset UpdatedAt { get; }

    /// <summary>Stable identity suitable for deduplicating repeated observations of this run.</summary>
    public string Identity => $"{Repository}#{RunId}";

    /// <summary>True only when the provider reported a completed successful run.</summary>
    public bool IsSuccessful => Status == CiRunStatus.Completed && Conclusion == CiRunConclusion.Success;
}
