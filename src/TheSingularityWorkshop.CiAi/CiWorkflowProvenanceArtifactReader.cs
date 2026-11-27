using System.IO.Compression;
using System.Security.Cryptography;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Verified workflow-provenance artifact bytes bound to GitHub's independent artifact metadata
/// and one exact source workflow run attempt.
/// </summary>
public sealed record CiWorkflowProvenanceArtifactEvidence(
    CiProviderArtifactObservation ProviderArtifact,
    CiWorkflowProvenanceRecord Provenance,
    string PayloadSha256,
    int PayloadSizeBytes)
{
    public bool PullRequestHeadBaseVerified { get; init; }
}

public static class CiWorkflowProvenanceArtifactReader
{
    public const int MaximumPayloadBytes = 1024 * 1024;

    public static CiWorkflowProvenanceArtifactEvidence Read(
        ReadOnlyMemory<byte> archiveBytes,
        CiProviderArtifactObservation providerArtifact,
        string expectedArtifactName,
        string expectedEntryName,
        string expectedRepository,
        long expectedRunId,
        int expectedRunAttempt,
        string expectedHeadSha,
        string expectedWorkflowRef,
        string expectedWorkflowSha,
        string expectedEventName,
        string expectedEventRef)
        => ReadCore(archiveBytes, providerArtifact, expectedArtifactName, expectedEntryName,
            expectedRepository, expectedRunId, expectedRunAttempt, expectedHeadSha, expectedHeadSha,
            expectedHeadSha, expectedWorkflowPath: ".github/workflows/ci.yml", expectedWorkflowRef,
            expectedWorkflowSha, expectedEventName, expectedEventRef, expectedEventRef, expectedPullRequest: null);

    /// <summary>
    /// Reads an external workflow profile. Provider head, event SHA, and checkout SHA remain separate
    /// arguments so PR merge runs cannot collapse source-head and merge-checkout identities.
    /// The host must independently observe each supplied identity.
    /// </summary>
    public static CiWorkflowProvenanceArtifactEvidence Read(
        ReadOnlyMemory<byte> archiveBytes,
        CiProviderArtifactObservation providerArtifact,
        CiWorkflowObserverProfile profile,
        string expectedRepository,
        long expectedRunId,
        int expectedRunAttempt,
        string expectedProviderHeadSha,
        string expectedEventSha,
        string expectedCheckoutSha,
        string expectedWorkflowRef,
        string expectedWorkflowSha,
        string expectedEventName,
        string expectedEventRef,
        CiProviderPullRequestObservation? expectedPullRequest = null)
        => Read(archiveBytes, providerArtifact, profile, expectedRepository, expectedRunId,
            expectedRunAttempt, expectedProviderHeadSha, expectedEventSha, expectedEventRef,
            expectedCheckoutSha, expectedWorkflowRef, expectedWorkflowSha, expectedEventName,
            expectedEventRef, expectedPullRequest);

    /// <summary>
    /// Reads a provenance artifact when the event ref and actual checked-out candidate ref differ,
    /// as they do for trusted workflow_dispatch runs that check out an isolated feature branch.
    /// Both refs must be independently bound by the host to provider run and dispatch intent.
    /// </summary>
    public static CiWorkflowProvenanceArtifactEvidence Read(
        ReadOnlyMemory<byte> archiveBytes,
        CiProviderArtifactObservation providerArtifact,
        CiWorkflowObserverProfile profile,
        string expectedRepository,
        long expectedRunId,
        int expectedRunAttempt,
        string expectedProviderHeadSha,
        string expectedEventSha,
        string expectedCheckoutRef,
        string expectedCheckoutSha,
        string expectedWorkflowRef,
        string expectedWorkflowSha,
        string expectedEventName,
        string expectedEventRef,
        CiProviderPullRequestObservation? expectedPullRequest = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return ReadCore(archiveBytes, providerArtifact,
            profile.ProvenanceArtifactName(expectedRunId, expectedRunAttempt),
            profile.ProvenanceEntryName, expectedRepository, expectedRunId, expectedRunAttempt,
            expectedProviderHeadSha, expectedEventSha, expectedCheckoutRef, expectedCheckoutSha,
            profile.WorkflowPath, expectedWorkflowRef, expectedWorkflowSha, expectedEventName,
            expectedEventRef, expectedPullRequest);
    }

    private static CiWorkflowProvenanceArtifactEvidence ReadCore(
        ReadOnlyMemory<byte> archiveBytes,
        CiProviderArtifactObservation providerArtifact,
        string expectedArtifactName,
        string expectedEntryName,
        string expectedRepository,
        long expectedRunId,
        int expectedRunAttempt,
        string expectedProviderHeadSha,
        string expectedEventSha,
        string expectedCheckoutRef,
        string expectedCheckoutSha,
        string expectedWorkflowPath,
        string expectedWorkflowRef,
        string expectedWorkflowSha,
        string expectedEventName,
        string expectedEventRef,
        CiProviderPullRequestObservation? expectedPullRequest)
    {
        ArgumentNullException.ThrowIfNull(providerArtifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedArtifactName);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedEntryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRepository);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedWorkflowRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedEventName);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedEventRef);
        ValidateSha(expectedProviderHeadSha, nameof(expectedProviderHeadSha));
        ValidateSha(expectedEventSha, nameof(expectedEventSha));
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedCheckoutRef);
        if (expectedCheckoutRef.Length > 512 || expectedCheckoutRef.Any(char.IsControl) ||
            expectedCheckoutRef.Any(char.IsWhiteSpace) || !expectedCheckoutRef.StartsWith("refs/", StringComparison.Ordinal))
            throw new ArgumentException("Expected checkout ref must be a bounded fully-qualified provider ref.", nameof(expectedCheckoutRef));
        ValidateSha(expectedCheckoutSha, nameof(expectedCheckoutSha));
        ValidateSha(expectedWorkflowSha, nameof(expectedWorkflowSha));
        if (expectedRunId <= 0) throw new ArgumentOutOfRangeException(nameof(expectedRunId));
        if (expectedRunAttempt <= 0) throw new ArgumentOutOfRangeException(nameof(expectedRunAttempt));
        ValidateLeafName(expectedEntryName);

        if (providerArtifact.IsExpired || providerArtifact.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidDataException("Workflow provenance artifact is expired.");
        if (!string.Equals(providerArtifact.Repository, expectedRepository, StringComparison.OrdinalIgnoreCase) ||
            providerArtifact.RunId != expectedRunId ||
            !string.Equals(providerArtifact.HeadSha, expectedProviderHeadSha, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(providerArtifact.ArtifactName, expectedArtifactName, StringComparison.Ordinal))
            throw new InvalidDataException("Provider artifact metadata does not match the exact expected repository, run, SHA, and artifact name.");
        if (archiveBytes.IsEmpty || archiveBytes.Length != providerArtifact.ArchiveSizeBytes ||
            archiveBytes.Length > CiTestResultArtifactArchive.DefaultMaximumArchiveBytes)
            throw new InvalidDataException("Workflow provenance archive size is empty, oversized, or differs from provider metadata.");

        var expectedArchiveDigest = ParseProviderDigest(providerArtifact.ProviderArchiveDigest);
        var actualArchiveDigest = SHA256.HashData(archiveBytes.Span);
        if (!CryptographicOperations.FixedTimeEquals(actualArchiveDigest, expectedArchiveDigest))
            throw new InvalidDataException("Workflow provenance ZIP digest does not match the provider-observed archive digest.");

        using var stream = new MemoryStream(archiveBytes.ToArray(), writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count != 1)
            throw new InvalidDataException("Workflow provenance artifact must contain exactly one payload entry.");
        var entry = archive.Entries[0];
        ValidateLeafName(entry.FullName);
        if (!string.Equals(entry.FullName, expectedEntryName, StringComparison.Ordinal) ||
            entry.Length is <= 0 or > MaximumPayloadBytes ||
            entry.CompressedLength <= 0 ||
            entry.Length > (long)entry.CompressedLength * CiTestResultArtifactArchive.MaximumCompressionRatio)
            throw new InvalidDataException("Workflow provenance ZIP entry has an unexpected name or exceeds its bounded size/compression policy.");

        var payload = ReadBounded(entry);
        var payloadDigest = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var provenance = CiWorkflowProvenanceRecord.ParseAndValidate(
            payload, expectedRepository, expectedRunId, expectedRunAttempt, payloadDigest);
        var pullRequestHeadBaseVerified = provenance.PullRequest is null;
        if (provenance.PullRequest is not null && expectedPullRequest is not null)
        {
            if (provenance.PullRequest.Number != expectedPullRequest.Number ||
                !string.Equals(provenance.PullRequest.HeadRef, expectedPullRequest.HeadRef, StringComparison.Ordinal) ||
                !string.Equals(provenance.PullRequest.HeadSha, expectedPullRequest.HeadSha, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(provenance.PullRequest.BaseRef, expectedPullRequest.BaseRef, StringComparison.Ordinal) ||
                !string.Equals(provenance.PullRequest.BaseSha, expectedPullRequest.BaseSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Pull-request head/base identities do not match the independent provider observation.");
            pullRequestHeadBaseVerified = true;
        }

        var expectedFullWorkflowRef = expectedRepository + "/" + expectedWorkflowPath + "@" + expectedWorkflowRef;
        if (!string.Equals(provenance.WorkflowPath, expectedWorkflowPath, StringComparison.Ordinal) ||
            !string.Equals(provenance.WorkflowRef, expectedFullWorkflowRef, StringComparison.Ordinal) ||
            !string.Equals(provenance.WorkflowSha, expectedWorkflowSha, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(provenance.EventName, expectedEventName, StringComparison.Ordinal) ||
            !string.Equals(provenance.EventRef, expectedEventRef, StringComparison.Ordinal) ||
            !string.Equals(provenance.EventSha, expectedEventSha, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(provenance.CheckoutRef, expectedCheckoutRef, StringComparison.Ordinal) ||
            !string.Equals(provenance.CheckoutSha, expectedCheckoutSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Workflow provenance does not match the independently trusted workflow, event, and checkout policy.");

        return new CiWorkflowProvenanceArtifactEvidence(
            providerArtifact, provenance, payloadDigest, payload.Length)
        {
            PullRequestHeadBaseVerified = pullRequestHeadBaseVerified
        };
    }

    private static byte[] ReadBounded(ZipArchiveEntry entry)
    {
        using var input = entry.Open();
        using var output = new MemoryStream((int)entry.Length);
        var buffer = new byte[81920];
        var total = 0;
        while (true)
        {
            var read = input.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            total = checked(total + read);
            if (total > MaximumPayloadBytes || total > entry.Length)
                throw new InvalidDataException("Workflow provenance entry expanded beyond its declared or configured size.");
            output.Write(buffer, 0, read);
        }
        if (total != entry.Length)
            throw new InvalidDataException("Workflow provenance entry length differs from its ZIP metadata.");
        return output.ToArray();
    }

    private static byte[] ParseProviderDigest(string value)
    {
        if (value.Length != 71 || !value.StartsWith("sha256:", StringComparison.Ordinal) ||
            !value.AsSpan(7).ToString().All(Uri.IsHexDigit))
            throw new InvalidDataException("Provider artifact digest is not a canonical SHA-256 value.");
        return Convert.FromHexString(value[7..]);
    }

    private static void ValidateSha(string value, string parameterName)
    {
        if (value is not { Length: 40 } || !value.All(Uri.IsHexDigit))
            throw new ArgumentException("Expected a full 40-character hexadecimal commit SHA.", parameterName);
    }

    private static void ValidateLeafName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 ||
            value is "." or ".." || value.Contains('/') || value.Contains('\\') ||
            value.Contains(':') || value.Any(char.IsControl) || Path.IsPathRooted(value))
            throw new InvalidDataException("Workflow provenance entry name is not a safe leaf filename.");
    }
}
