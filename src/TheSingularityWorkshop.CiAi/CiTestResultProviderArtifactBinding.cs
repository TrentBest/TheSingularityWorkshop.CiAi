namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Host-observed binding between one provider artifact and one exact workflow job.
/// Construct this only after independently retrieving the provider artifact/job records
/// and applying the host's explicit artifact-to-job association policy. Do not populate it
/// from fields in ciai.test-results.v1.
/// </summary>
public sealed record CiTestResultProviderArtifactBinding
{
    internal CiTestResultProviderArtifactBinding(
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

    /// <summary>
    /// Binds provider-observed artifact metadata to one exact job from the trusted run packet.
    /// The required-job policy must declare the exact artifact name for this job key.
    /// </summary>
    public static CiTestResultProviderArtifactBinding FromObservedArtifact(
        CiProviderArtifactObservation artifact,
        CiWorkflowEvidencePacket evidence,
        CiRequiredJobPolicy requiredJobPolicy,
        long jobId,
        string jobKey)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(requiredJobPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobKey);
        if (!string.Equals(artifact.Repository, evidence.Provenance.Repository, StringComparison.OrdinalIgnoreCase) ||
            artifact.RunId != evidence.Provenance.RunId ||
            !string.Equals(artifact.HeadSha, evidence.ObservedRun.WorkflowSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Provider artifact does not belong to the exact trusted repository and workflow run.");

        var definitions = requiredJobPolicy.RequiredJobs.Where(definition =>
            string.Equals(definition.WorkflowPath, evidence.Provenance.WorkflowPath, StringComparison.Ordinal) &&
            string.Equals(definition.JobKey, jobKey, StringComparison.Ordinal) &&
            string.Equals(definition.ArtifactName, artifact.ArtifactName, StringComparison.Ordinal)).ToArray();
        if (definitions.Length != 1 || string.IsNullOrWhiteSpace(definitions[0].ArtifactName))
            throw new InvalidDataException("Provider artifact name does not match exactly one configured required job.");

        var jobs = evidence.ObservedRun.Jobs.Where(job =>
            job.JobId == jobId &&
            job.RunId == evidence.Provenance.RunId &&
            job.RunAttempt == evidence.Provenance.RunAttempt &&
            string.Equals(job.Name, definitions[0].JobName, StringComparison.Ordinal)).ToArray();
        if (jobs.Length != 1)
            throw new InvalidDataException("Provider artifact binding does not identify exactly one observed job in the exact run attempt.");
        if (!jobs[0].StartedAt.HasValue || !jobs[0].CompletedAt.HasValue ||
            artifact.CreatedAt < jobs[0].StartedAt.Value || artifact.CreatedAt > jobs[0].CompletedAt.Value)
            throw new InvalidDataException("Provider artifact creation time does not fall within the exact observed job attempt.");
        var requiredStatus = requiredJobPolicy.Evaluate(evidence, jobId, jobKey);
        if (requiredStatus is not (CiRequiredCheckStatus.Passed or CiRequiredCheckStatus.Failed))
            throw new InvalidDataException("Provider artifact binding does not identify a complete required check.");

        return new CiTestResultProviderArtifactBinding(
            artifact.Repository, artifact.RunId, evidence.Provenance.RunAttempt,
            jobId, jobs[0].Name, jobKey, artifact.ArtifactId, artifact.ArtifactName,
            artifact.ProviderArchiveDigest, artifact.ArchiveSizeBytes,
            artifact.CreatedAt, artifact.ExpiresAt, artifact.IsExpired);
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
