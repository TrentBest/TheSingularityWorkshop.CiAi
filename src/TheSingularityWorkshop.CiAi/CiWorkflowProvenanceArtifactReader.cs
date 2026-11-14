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
    int PayloadSizeBytes);

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
    {
        ArgumentNullException.ThrowIfNull(providerArtifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedArtifactName);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedEntryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRepository);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedWorkflowRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedEventName);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedEventRef);
        ValidateSha(expectedHeadSha, nameof(expectedHeadSha));
        ValidateSha(expectedWorkflowSha, nameof(expectedWorkflowSha));
        if (expectedRunId <= 0) throw new ArgumentOutOfRangeException(nameof(expectedRunId));
        if (expectedRunAttempt <= 0) throw new ArgumentOutOfRangeException(nameof(expectedRunAttempt));
        ValidateLeafName(expectedEntryName);

        if (providerArtifact.IsExpired || providerArtifact.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidDataException("Workflow provenance artifact is expired.");
        if (!string.Equals(providerArtifact.Repository, expectedRepository, StringComparison.OrdinalIgnoreCase) ||
            providerArtifact.RunId != expectedRunId ||
            !string.Equals(providerArtifact.HeadSha, expectedHeadSha, StringComparison.OrdinalIgnoreCase) ||
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

        var expectedFullWorkflowRef = expectedRepository + "/.github/workflows/ci.yml@" + expectedWorkflowRef;
        if (!string.Equals(provenance.WorkflowPath, ".github/workflows/ci.yml", StringComparison.Ordinal) ||
            !string.Equals(provenance.WorkflowRef, expectedFullWorkflowRef, StringComparison.Ordinal) ||
            !string.Equals(provenance.WorkflowSha, expectedWorkflowSha, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(provenance.EventName, expectedEventName, StringComparison.Ordinal) ||
            !string.Equals(provenance.EventRef, expectedEventRef, StringComparison.Ordinal) ||
            !string.Equals(provenance.EventSha, expectedHeadSha, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(provenance.CheckoutRef, expectedEventRef, StringComparison.Ordinal) ||
            !string.Equals(provenance.CheckoutSha, expectedHeadSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Workflow provenance does not match the independently trusted workflow, event, and checkout policy.");

        return new CiWorkflowProvenanceArtifactEvidence(
            providerArtifact, provenance, payloadDigest, payload.Length);
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
