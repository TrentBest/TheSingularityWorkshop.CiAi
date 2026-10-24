using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Reads a payload from a caller-authenticated stream with a strict in-memory size bound, then verifies it.</summary>
public static class ArtifactPayloadReader
{
    /// <summary>
    /// Reads at most <paramref name="maximumPayloadBytes"/> into memory and verifies the completed bytes
    /// against the manifest. This method does not retrieve a URI, authenticate a provider, or persist data.
    /// The caller owns and must dispose <paramref name="source"/>.
    /// </summary>
    public static async Task<ArtifactPayloadReadResult> ReadAndVerifyAsync(
        Stream source,
        ArtifactManifest manifest,
        long maximumPayloadBytes = ArtifactPayloadVerifier.DefaultMaximumPayloadBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(manifest);
        if (!source.CanRead)
            throw new ArgumentException("The source stream must be readable.", nameof(source));
        if (maximumPayloadBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPayloadBytes));

        // MemoryStream is int-indexed; reject limits that cannot be represented by a managed byte array.
        var effectiveLimit = Math.Min(maximumPayloadBytes, int.MaxValue);
        using var buffer = new MemoryStream((int)Math.Min(manifest.SizeBytes, Math.Min(effectiveLimit, 81920L)));
        var chunk = new byte[81920];
        long total = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await source.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;

            if (total > effectiveLimit - read)
            {
                return ArtifactPayloadReadResult.Rejected(
                    ArtifactPayloadVerificationResult.Rejected("payload.exceeds-limit", total + read));
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            total += read;
        }

        var payload = buffer.ToArray();
        var verification = ArtifactPayloadVerifier.Verify(manifest, payload, effectiveLimit);
        return verification.IsVerified
            ? ArtifactPayloadReadResult.Verified(payload, verification)
            : ArtifactPayloadReadResult.Rejected(verification);
    }
}

/// <summary>Payload bytes and integrity outcome produced by a bounded stream read.</summary>
public sealed record ArtifactPayloadReadResult
{
    private ArtifactPayloadReadResult(byte[]? payload, ArtifactPayloadVerificationResult verification)
    {
        Payload = payload;
        Verification = verification;
    }

    /// <summary>Verified payload bytes, or null when the read or verification was rejected.</summary>
    public ReadOnlyMemory<byte>? Payload { get; }

    /// <summary>Integrity verification result, including a stable rejection code.</summary>
    public ArtifactPayloadVerificationResult Verification { get; }

    internal static ArtifactPayloadReadResult Verified(byte[] payload, ArtifactPayloadVerificationResult verification) =>
        new(payload, verification);

    internal static ArtifactPayloadReadResult Rejected(ArtifactPayloadVerificationResult verification) =>
        new(null, verification);
}
