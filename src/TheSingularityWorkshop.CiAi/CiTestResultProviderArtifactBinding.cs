namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Host-observed binding between one provider artifact and one exact workflow job.
/// Construct this only after independently retrieving the provider artifact/job records
/// and applying the host's explicit artifact-to-job association policy. Do not populate it
/// from fields in ciai.test-results.v1.
/// </summary>
public sealed record CiTestResultProviderArtifactBinding
{
    public CiTestResultProviderArtifactBinding(
        string repository,
        long runId,
        int runAttempt,
        long jobId,
        string jobName,
        string jobKey,
        long artifactId,
        string artifactName,
        string providerArchiveDigest,
        long archiveSizeBytes,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        bool isExpired)
    {
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Provider artifact repository must use canonical owner/repository identity.", nameof(repository));
        if (runId <= 0 || runAttempt <= 0 || jobId <= 0 || artifactId <= 0)
            throw new ArgumentOutOfRangeException(nameof(runId), "Run, attempt, job, and artifact identifiers must be positive.");
        RequireText(jobName, nameof(jobName), 200);
        RequireIdentifier(jobKey, nameof(jobKey), 100);
        RequireText(artifactName, nameof(artifactName), 200);
        if (providerArchiveDigest is null || providerArchiveDigest.Length != 71 ||
            !providerArchiveDigest.StartsWith("sha256:", StringComparison.Ordinal) ||
            !providerArchiveDigest.AsSpan(7).ToString().All(Uri.IsHexDigit))
            throw new ArgumentException("Provider archive digest must use sha256:<64 hexadecimal characters>.", nameof(providerArchiveDigest));
        if (archiveSizeBytes is <= 0 or > CiTestResultArtifactArchive.DefaultMaximumArchiveBytes)
            throw new ArgumentOutOfRangeException(nameof(archiveSizeBytes));
        if (createdAt == default || expiresAt <= createdAt)
            throw new ArgumentException("Provider artifact creation and expiry timestamps are invalid.");

        Repository = repository;
        RunId = runId;
        RunAttempt = runAttempt;
        JobId = jobId;
        JobName = jobName;
        JobKey = jobKey;
        ArtifactId = artifactId;
        ArtifactName = artifactName;
        ProviderArchiveDigest = providerArchiveDigest.ToLowerInvariant();
        ArchiveSizeBytes = archiveSizeBytes;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        IsExpired = isExpired;
    }

    public string Repository { get; }
    public long RunId { get; }
    public int RunAttempt { get; }
    public long JobId { get; }
    public string JobName { get; }
    public string JobKey { get; }
    public long ArtifactId { get; }
    public string ArtifactName { get; }
    public string ProviderArchiveDigest { get; }
    public long ArchiveSizeBytes { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
    public bool IsExpired { get; }

    private static void RequireText(string value, string name, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Length > maximumLength || value.Any(char.IsControl))
            throw new ArgumentException($"Value must be at most {maximumLength} characters without control characters.", name);
    }

    private static void RequireIdentifier(string value, string name, int maximumLength)
    {
        RequireText(value, name, maximumLength);
        if (!value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
            throw new ArgumentException("Identifier contains unsupported characters.", name);
    }
}
