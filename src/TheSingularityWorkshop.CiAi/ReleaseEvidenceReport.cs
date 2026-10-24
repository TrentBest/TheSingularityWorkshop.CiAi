namespace TheSingularityWorkshop.CiAi;

/// <summary>Evidence state normalized for reporting while retaining raw provider facts.</summary>
public enum ReleaseEvidenceState
{
    Passed = 0,
    Failed = 1,
    Pending = 2,
    Skipped = 3,
    Missing = 4,
    Unknown = 5,
    Unverified = 6,
    Stale = 7
}

/// <summary>Release-gate evidence categories; categories describe requirements, not workflow names.</summary>
public enum ReleaseEvidenceCategory
{
    BuildAndTests = 0,
    PublicPackageCompatibility = 1,
    SourceIntegration = 2,
    Security = 3,
    PackageInspection = 4,
    DocumentationAndVersion = 5,
    PublicationGate = 6,
    BranchReconciliation = 7,
    OwnerAuthorization = 8
}

/// <summary>
/// One immutable evidence item tied to a single repository and exact commit.
/// Raw provider values are retained; normalized state is never inferred from prose.
/// </summary>
public sealed record ReleaseEvidenceRecord
{
    public ReleaseEvidenceRecord(
        string repository,
        string headSha,
        ReleaseEvidenceCategory category,
        ReleaseEvidenceState state,
        string rawStatus,
        string rawConclusion,
        DateTimeOffset retrievedAt,
        Uri? evidenceUrl = null,
        long? runId = null,
        int? runAttempt = null,
        int? pullRequestNumber = null,
        string? workflowIdentity = null,
        string? jobIdentity = null,
        string? diagnostic = null,
        string? artifactName = null,
        string? artifactDigest = null,
        string? artifactDigestScope = null,
        bool directlyVerified = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(headSha);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawStatus);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawConclusion);
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must use canonical owner/repository identifiers.", nameof(repository));
        if (headSha.Length != 40 || !headSha.All(Uri.IsHexDigit))
            throw new ArgumentException("Head SHA must be a full 40-character hexadecimal Git commit identifier.", nameof(headSha));
        if (!Enum.IsDefined(category)) throw new ArgumentOutOfRangeException(nameof(category));
        if (!Enum.IsDefined(state)) throw new ArgumentOutOfRangeException(nameof(state));
        if (runId is <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        if (runAttempt is <= 0) throw new ArgumentOutOfRangeException(nameof(runAttempt));
        if (pullRequestNumber is <= 0) throw new ArgumentOutOfRangeException(nameof(pullRequestNumber));
        if (evidenceUrl is not null && (evidenceUrl.Scheme != Uri.UriSchemeHttps ||
            !evidenceUrl.IsDefaultPort || !string.IsNullOrEmpty(evidenceUrl.UserInfo) ||
            !string.IsNullOrEmpty(evidenceUrl.Fragment)))
            throw new ArgumentException("Evidence URL must use HTTPS/default port without credentials or fragments.", nameof(evidenceUrl));
        if (artifactDigest is not null &&
            (artifactDigest.Length != 64 || !artifactDigest.All(Uri.IsHexDigit)))
            throw new ArgumentException("Artifact digest must be a 64-character SHA-256 hexadecimal value.", nameof(artifactDigest));
        if (artifactDigest is not null && string.IsNullOrWhiteSpace(artifactDigestScope))
            throw new ArgumentException("Artifact digest scope is required when a digest is supplied.", nameof(artifactDigestScope));
        if (state == ReleaseEvidenceState.Passed && !directlyVerified)
            throw new ArgumentException("Unverified evidence cannot be marked passed.", nameof(directlyVerified));

        Repository = repository;
        HeadSha = headSha.ToLowerInvariant();
        Category = category;
        State = state;
        RawStatus = rawStatus;
        RawConclusion = rawConclusion;
        RetrievedAt = retrievedAt;
        EvidenceUrl = evidenceUrl;
        RunId = runId;
        RunAttempt = runAttempt;
        PullRequestNumber = pullRequestNumber;
        WorkflowIdentity = workflowIdentity;
        JobIdentity = jobIdentity;
        Diagnostic = diagnostic;
        ArtifactName = artifactName;
        ArtifactDigest = artifactDigest?.ToLowerInvariant();
        ArtifactDigestScope = artifactDigestScope;
        DirectlyVerified = directlyVerified;
    }

    public string Repository { get; }
    public string HeadSha { get; }
    public ReleaseEvidenceCategory Category { get; }
    public ReleaseEvidenceState State { get; }
    public string RawStatus { get; }
    public string RawConclusion { get; }
    public DateTimeOffset RetrievedAt { get; }
    public Uri? EvidenceUrl { get; }
    public long? RunId { get; }
    public int? RunAttempt { get; }
    public int? PullRequestNumber { get; }
    public string? WorkflowIdentity { get; }
    public string? JobIdentity { get; }
    public string? Diagnostic { get; }
    public string? ArtifactName { get; }
    public string? ArtifactDigest { get; }
    public string? ArtifactDigestScope { get; }
    public bool DirectlyVerified { get; }
}

/// <summary>Conservative report for one exact repository/SHA pair.</summary>
public sealed record ReleaseEvidenceReport
{
    private ReleaseEvidenceReport(string repository, string headSha,
        IReadOnlyDictionary<ReleaseEvidenceCategory, ReleaseEvidenceState> categories,
        IReadOnlyList<ReleaseEvidenceRecord> records)
    {
        Repository = repository;
        HeadSha = headSha;
        Categories = categories;
        Records = records;
    }

    public string Repository { get; }
    public string HeadSha { get; }
    public IReadOnlyDictionary<ReleaseEvidenceCategory, ReleaseEvidenceState> Categories { get; }
    public IReadOnlyList<ReleaseEvidenceRecord> Records { get; }

    /// <summary>True only when every requested category has directly verified passing evidence.</summary>
    public bool IsPassing => Categories.Count > 0 && Categories.Values.All(x => x == ReleaseEvidenceState.Passed);

    /// <summary>
    /// Builds a report only from records matching the requested repository and full commit SHA.
    /// Missing requirements remain Missing; mismatched records are rejected rather than silently mixed.
    /// </summary>
    public static ReleaseEvidenceReport Aggregate(
        string repository,
        string headSha,
        IEnumerable<ReleaseEvidenceCategory> requiredCategories,
        IEnumerable<ReleaseEvidenceRecord> records)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(headSha);
        ArgumentNullException.ThrowIfNull(requiredCategories);
        ArgumentNullException.ThrowIfNull(records);
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must use canonical owner/repository identifiers.", nameof(repository));
        if (headSha.Length != 40 || !headSha.All(Uri.IsHexDigit))
            throw new ArgumentException("Head SHA must be a full 40-character hexadecimal Git commit identifier.", nameof(headSha));

        var required = requiredCategories.Distinct().ToArray();
        if (required.Length == 0 || required.Any(x => !Enum.IsDefined(x)))
            throw new ArgumentException("At least one valid required evidence category is necessary.", nameof(requiredCategories));

        var matching = records.ToArray();
        if (matching.Any(x => x is null))
            throw new ArgumentException("Evidence collection cannot contain null records.", nameof(records));
        var mismatched = matching.FirstOrDefault(x =>
            !string.Equals(x.Repository, repository, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(x.HeadSha, headSha, StringComparison.OrdinalIgnoreCase));
        if (mismatched is not null)
            throw new ArgumentException("Evidence records must all match the exact requested repository and commit SHA.", nameof(records));

        var summary = new Dictionary<ReleaseEvidenceCategory, ReleaseEvidenceState>();
        foreach (var category in required)
        {
            var categoryRecords = matching.Where(x => x.Category == category).ToArray();
            if (categoryRecords.Length == 0)
            {
                summary[category] = ReleaseEvidenceState.Missing;
                continue;
            }

            // Conservative: any non-passing, stale, skipped, or unverified record keeps the category non-passing.
            summary[category] = categoryRecords.All(x =>
                x.State == ReleaseEvidenceState.Passed && x.DirectlyVerified)
                ? ReleaseEvidenceState.Passed
                : categoryRecords.First(x => x.State != ReleaseEvidenceState.Passed || !x.DirectlyVerified).State;
        }

        return new ReleaseEvidenceReport(repository, headSha.ToLowerInvariant(),
            new System.Collections.ObjectModel.ReadOnlyDictionary<ReleaseEvidenceCategory, ReleaseEvidenceState>(summary),
            Array.AsReadOnly(matching));
    }
}
