using System.Text;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Outcome of recording an artifact receipt in a durable local ledger.</summary>
public enum ArtifactReceiptRecordOutcome
{
    Recorded,
    Duplicate,
    Conflict,
    Rejected
}

/// <summary>
/// Append-only, dependency-free receipt ledger. It durably suppresses retries by deduplication key
/// and rejects reuse of a key for different receipt content. It does not authorize artifact deletion.
/// </summary>
public sealed class FileArtifactReceiptLedger
{
    private readonly string _path;

    public FileArtifactReceiptLedger(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    /// <summary>
    /// Reconciles the receipt against its exact manifest before recording it. A conflicting duplicate
    /// key is reported rather than overwriting the original receipt. Concurrent processes serialize
    /// writes through an exclusive sidecar lock file.
    /// </summary>
    public ArtifactReceiptRecordOutcome Record(
        ArtifactReceiptAcknowledgement receipt,
        ArtifactManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(manifest);

        if (receipt.VerifyAgainst(manifest).Count != 0)
            return ArtifactReceiptRecordOutcome.Rejected;

        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var lockPath = _path + ".lock";

        // FileShare.None provides a cross-process exclusion boundary. A caller that encounters
        // another writer receives IOException and can retry according to its own bounded policy.
        using var fileLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var existing = ReadCompleteLinesAndRepairTail();

        var key = Encode(receipt.DeduplicationKey);
        var payload = Encode(SerializeReceipt(receipt));
        foreach (var line in existing)
        {
            var separator = line.IndexOf('\t');
            if (separator <= 0)
                throw new InvalidDataException("Receipt ledger contains a malformed record.");
            if (!string.Equals(line[..separator], key, StringComparison.Ordinal))
                continue;

            return string.Equals(line[(separator + 1)..], payload, StringComparison.Ordinal)
                ? ArtifactReceiptRecordOutcome.Duplicate
                : ArtifactReceiptRecordOutcome.Conflict;
        }

        using (var output = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            var record = Encoding.UTF8.GetBytes(key + "\t" + payload + "\n");
            output.Write(record);
            output.Flush(flushToDisk: true);
        }

        return ArtifactReceiptRecordOutcome.Recorded;
    }

    private string[] ReadCompleteLinesAndRepairTail()
    {
        if (!File.Exists(_path))
            return Array.Empty<string>();

        using var input = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        if (input.Length == 0)
            return Array.Empty<string>();

        if (input.Length > int.MaxValue)
            throw new InvalidDataException("Receipt ledger exceeds the supported 2 GiB in-memory scan limit.");

        var bytes = new byte[(int)input.Length];
        input.ReadExactly(bytes);
        if (bytes[^1] != (byte)'\n')
        {
            var lastNewline = Array.LastIndexOf(bytes, (byte)'\n');
            input.SetLength(lastNewline < 0 ? 0 : lastNewline + 1);
            Array.Resize(ref bytes, lastNewline < 0 ? 0 : lastNewline + 1);
            input.Flush(flushToDisk: true);
        }

        return Encoding.UTF8.GetString(bytes)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string SerializeReceipt(ArtifactReceiptAcknowledgement receipt)
    {
        var fields = new[]
        {
            receipt.ContractVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            receipt.AcknowledgementId,
            receipt.DeduplicationKey,
            receipt.RequestId,
            receipt.ArtifactId,
            receipt.ArtifactVersion,
            receipt.Digest,
            receipt.ClientIdentity,
            receipt.Retrieved ? "1" : "0",
            receipt.IntegrityVerified ? "1" : "0",
            receipt.AcknowledgedAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            receipt.LocalValidation.ToString(),
            receipt.ProviderRepository ?? string.Empty,
            receipt.ProviderRunId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            receipt.SourceSha ?? string.Empty
        };
        return string.Join('|', fields.Select(Encode));
    }

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
}
