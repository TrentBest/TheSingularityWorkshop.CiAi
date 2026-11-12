using System.IO.Compression;
using System.Security.Cryptography;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Verified in-memory contents of one provider workflow-artifact ZIP. The provider archive
/// digest is checked before any entry is read; extraction never writes to the filesystem.
/// </summary>
public sealed class CiTestResultArtifactArchive
{
    public const long DefaultMaximumArchiveBytes = 20 * 1024 * 1024;
    public const long DefaultMaximumPayloadBytes = CiTestResultReport.DefaultMaximumPayloadBytes;
    public const int MaximumEntries = 2;
    public const int MaximumCompressionRatio = 100;

    private CiTestResultArtifactArchive(
        string providerArchiveDigest,
        long archiveSizeBytes,
        string trxEntryName,
        byte[] trxBytes,
        string provenanceEntryName,
        byte[] provenanceBytes)
    {
        ProviderArchiveDigest = providerArchiveDigest;
        ArchiveSizeBytes = archiveSizeBytes;
        TrxEntryName = trxEntryName;
        TrxBytes = trxBytes;
        TrxSha256 = Convert.ToHexString(SHA256.HashData(trxBytes)).ToLowerInvariant();
        ProvenanceEntryName = provenanceEntryName;
        ProvenanceBytes = provenanceBytes;
        ProvenanceSha256 = Convert.ToHexString(SHA256.HashData(provenanceBytes)).ToLowerInvariant();
    }

    public string ProviderArchiveDigest { get; }
    public long ArchiveSizeBytes { get; }
    public string TrxEntryName { get; }
    public ReadOnlyMemory<byte> TrxBytes { get; }
    public string TrxSha256 { get; }
    public string ProvenanceEntryName { get; }
    public ReadOnlyMemory<byte> ProvenanceBytes { get; }
    public string ProvenanceSha256 { get; }

    /// <summary>
    /// Verifies the provider's SHA-256 over the complete ZIP and reads exactly the expected
    /// TRX and provenance files under strict entry, size, path, and expansion limits.
    /// </summary>
    public static CiTestResultArtifactArchive Read(
        ReadOnlyMemory<byte> archiveBytes,
        string providerArchiveDigest,
        string expectedTrxEntryName,
        string expectedProvenanceEntryName,
        long maximumArchiveBytes = DefaultMaximumArchiveBytes,
        long maximumPayloadBytes = DefaultMaximumPayloadBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerArchiveDigest);
        ValidateLeafName(expectedTrxEntryName, nameof(expectedTrxEntryName));
        ValidateLeafName(expectedProvenanceEntryName, nameof(expectedProvenanceEntryName));
        if (string.Equals(expectedTrxEntryName, expectedProvenanceEntryName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("TRX and provenance entry names must be distinct.");
        if (maximumArchiveBytes is <= 0 or > DefaultMaximumArchiveBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumArchiveBytes));
        if (maximumPayloadBytes is <= 0 or > DefaultMaximumPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumPayloadBytes));
        if (archiveBytes.IsEmpty || archiveBytes.Length > maximumArchiveBytes)
            throw new InvalidDataException("Artifact archive is empty or exceeds the configured byte limit.");

        var expectedDigest = ParseProviderDigest(providerArchiveDigest);
        var actualDigest = SHA256.HashData(archiveBytes.Span);
        if (!CryptographicOperations.FixedTimeEquals(actualDigest, expectedDigest))
            throw new InvalidDataException("Downloaded artifact ZIP does not match the provider-reported SHA-256 digest.");

        using var stream = new MemoryStream(archiveBytes.ToArray(), writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count != MaximumEntries)
            throw new InvalidDataException("Artifact archive must contain exactly one TRX and one provenance entry.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ZipArchiveEntry? trx = null;
        ZipArchiveEntry? provenance = null;
        foreach (var entry in archive.Entries)
        {
            ValidateEntry(entry, maximumPayloadBytes);
            if (!names.Add(entry.FullName))
                throw new InvalidDataException("Artifact archive contains duplicate entry names.");

            if (string.Equals(entry.FullName, expectedTrxEntryName, StringComparison.Ordinal))
                trx = entry;
            else if (string.Equals(entry.FullName, expectedProvenanceEntryName, StringComparison.Ordinal))
                provenance = entry;
            else
                throw new InvalidDataException("Artifact archive contains an unexpected entry.");
        }

        if (trx is null || provenance is null)
            throw new InvalidDataException("Artifact archive is missing its expected TRX or provenance entry.");

        var trxBytes = ReadBounded(trx, maximumPayloadBytes);
        var provenanceBytes = ReadBounded(provenance, maximumPayloadBytes);
        return new CiTestResultArtifactArchive(
            "sha256:" + Convert.ToHexString(actualDigest).ToLowerInvariant(),
            archiveBytes.Length,
            trx.FullName,
            trxBytes,
            provenance.FullName,
            provenanceBytes);
    }

    private static void ValidateEntry(ZipArchiveEntry entry, long maximumPayloadBytes)
    {
        ValidateLeafName(entry.FullName, nameof(entry));
        if (entry.Length < 0 || entry.Length > maximumPayloadBytes ||
            entry.CompressedLength < 0 || entry.CompressedLength > DefaultMaximumArchiveBytes)
            throw new InvalidDataException("Artifact entry exceeds the configured size limits.");

        if (entry.Length > 0 && entry.CompressedLength == 0)
            throw new InvalidDataException("Artifact entry has an invalid zero-length compressed representation.");
        if (entry.CompressedLength > 0 &&
            entry.Length > (long)entry.CompressedLength * MaximumCompressionRatio)
            throw new InvalidDataException("Artifact entry exceeds the maximum compression ratio.");

        var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
        if (unixType != 0 && unixType != 0x8000)
            throw new InvalidDataException("Artifact archive contains a directory, symbolic link, or special filesystem entry.");
        if ((entry.ExternalAttributes & 0x400) != 0)
            throw new InvalidDataException("Artifact archive contains a reparse-point entry.");
    }

    private static byte[] ReadBounded(ZipArchiveEntry entry, long maximumPayloadBytes)
    {
        using var input = entry.Open();
        using var output = new MemoryStream((int)Math.Min(entry.Length, maximumPayloadBytes));
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = input.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            total += read;
            if (total > maximumPayloadBytes || total > entry.Length)
                throw new InvalidDataException("Artifact entry expanded beyond its declared or configured size.");
            output.Write(buffer, 0, read);
        }

        if (total != entry.Length)
            throw new InvalidDataException("Artifact entry length does not match its ZIP directory metadata.");
        return output.ToArray();
    }

    private static byte[] ParseProviderDigest(string value)
    {
        if (value.Length != 71 || !value.StartsWith("sha256:", StringComparison.Ordinal) ||
            !value.AsSpan(7).ToString().All(Uri.IsHexDigit))
            throw new ArgumentException("Provider digest must use sha256:<64 hexadecimal characters>.", nameof(value));
        return Convert.FromHexString(value[7..]);
    }

    private static void ValidateLeafName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 200 || value is "." or ".." ||
            value.Contains('/') || value.Contains('\\') || value.Contains(':') ||
            value.Any(char.IsControl) || Path.IsPathRooted(value))
            throw new ArgumentException("Artifact entry names must be safe, bounded leaf filenames.", parameterName);
    }
}
