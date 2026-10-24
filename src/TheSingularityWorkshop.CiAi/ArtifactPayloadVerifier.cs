using System.Security.Cryptography;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Verifies retrieved artifact bytes against the immutable manifest without executing them.</summary>
public static class ArtifactPayloadVerifier
{
    /// <summary>Default maximum payload size accepted by the verifier (16 MiB).</summary>
    public const long DefaultMaximumPayloadBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Checks the bounded payload size, exact manifest size, and SHA-256 digest.
    /// This method never retrieves, persists, parses, or executes the payload.
    /// </summary>
    public static ArtifactPayloadVerificationResult Verify(
        ArtifactManifest manifest,
        ReadOnlyMemory<byte> payload,
        long maximumPayloadBytes = DefaultMaximumPayloadBytes)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (maximumPayloadBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPayloadBytes));

        var actualSize = payload.Length;
        if (actualSize > maximumPayloadBytes)
            return ArtifactPayloadVerificationResult.Rejected("payload.exceeds-limit", actualSize);

        if (actualSize != manifest.SizeBytes)
            return ArtifactPayloadVerificationResult.Rejected("payload.size-mismatch", actualSize);

        var actualDigest = Convert.ToHexString(SHA256.HashData(payload.Span)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualDigest),
                Convert.FromHexString(manifest.Digest)))
            return ArtifactPayloadVerificationResult.Rejected("payload.digest-mismatch", actualSize, actualDigest);

        return ArtifactPayloadVerificationResult.Verified(actualSize, actualDigest);
    }
}

/// <summary>Immutable outcome of a local artifact byte-integrity check.</summary>
public sealed record ArtifactPayloadVerificationResult
{
    private ArtifactPayloadVerificationResult(bool isVerified, string? failureCode, long actualSizeBytes, string? actualDigest)
    {
        IsVerified = isVerified;
        FailureCode = failureCode;
        ActualSizeBytes = actualSizeBytes;
        ActualDigest = actualDigest;
    }

    public bool IsVerified { get; }
    public string? FailureCode { get; }
    public long ActualSizeBytes { get; }
    public string? ActualDigest { get; }

    internal static ArtifactPayloadVerificationResult Verified(long size, string digest) =>
        new(true, null, size, digest);

    internal static ArtifactPayloadVerificationResult Rejected(string code, long size, string? digest = null) =>
        new(false, code, size, digest);
}
