using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Immutable, byte-stable notice delivery persisted before any remote send attempt.</summary>
public sealed record FoundryArtifactReadyNoticeDelivery
{
    public FoundryArtifactReadyNoticeDelivery(ArtifactReadyNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        Notice = notice;
        Payload = FoundryArtifactReadyNoticeClient.SerializeNotice(notice);
        PayloadSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Payload))).ToLowerInvariant();
        NoticeId = notice.NoticeId;
        IdempotencyKey = notice.Manifest.IdempotencyKey;
    }

    public ArtifactReadyNotice Notice { get; }
    public string NoticeId { get; }
    public string IdempotencyKey { get; }
    public string Payload { get; }
    public string PayloadSha256 { get; }
}

public enum FoundryArtifactNoticeEnqueueOutcome
{
    Enqueued,
    Duplicate,
    Conflict
}

/// <summary>
/// Durable append-only outbox for Foundry artifact-ready notices. Replays the exact serialized notice
/// after restart and suppresses delivery only when a matching remote durable receipt is recorded.
/// </summary>
public sealed class FileFoundryArtifactReadyNoticeOutbox
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly string _path;

    public FileFoundryArtifactReadyNoticeOutbox(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public FoundryArtifactNoticeEnqueueOutcome Enqueue(FoundryArtifactReadyNoticeDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        using var fileLock = AcquireFileLock();
        var records = ReadRecords();
        var byNotice = records.FirstOrDefault(item =>
            string.Equals(item.NoticeId, delivery.NoticeId, StringComparison.Ordinal));
        if (byNotice is not null)
            return string.Equals(byNotice.PayloadSha256, delivery.PayloadSha256, StringComparison.Ordinal)
                ? FoundryArtifactNoticeEnqueueOutcome.Duplicate
                : FoundryArtifactNoticeEnqueueOutcome.Conflict;

        var byKey = records.FirstOrDefault(item =>
            string.Equals(item.IdempotencyKey, delivery.IdempotencyKey, StringComparison.Ordinal));
        if (byKey is not null)
            return FoundryArtifactNoticeEnqueueOutcome.Conflict;

        var record = new NoticeRecord(delivery.NoticeId, delivery.IdempotencyKey,
            delivery.Payload, delivery.PayloadSha256);
        AppendRecord(record);
        return FoundryArtifactNoticeEnqueueOutcome.Enqueued;
    }

    public IReadOnlyList<FoundryArtifactReadyNoticeDelivery> ReadPending(
        IReadOnlySet<string>? acknowledgedNoticeIds = null)
    {
        using var fileLock = AcquireFileLock();
        var pending = ReadRecords()
            .Where(item => acknowledgedNoticeIds is null || !acknowledgedNoticeIds.Contains(item.NoticeId))
            .Select(item =>
            {
                var notice = JsonSerializer.Deserialize<ArtifactReadyNotice>(item.Payload)
                    ?? throw new InvalidDataException("Notice outbox contains an empty notice payload.");
                var delivery = new FoundryArtifactReadyNoticeDelivery(notice);
                if (!string.Equals(delivery.NoticeId, item.NoticeId, StringComparison.Ordinal) ||
                    !string.Equals(delivery.IdempotencyKey, item.IdempotencyKey, StringComparison.Ordinal) ||
                    !string.Equals(delivery.PayloadSha256, item.PayloadSha256, StringComparison.Ordinal) ||
                    !string.Equals(delivery.Payload, item.Payload, StringComparison.Ordinal))
                    throw new InvalidDataException("Notice outbox identity or payload integrity validation failed.");
                return delivery;
            })
            .ToArray();
        return Array.AsReadOnly(pending);
    }

    private FileStream AcquireFileLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        return new FileStream(_path + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
    }

    private NoticeRecord[] ReadRecords()
    {
        if (!File.Exists(_path)) return Array.Empty<NoticeRecord>();
        using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (input.Length == 0) return Array.Empty<NoticeRecord>();
        if (input.Length > int.MaxValue)
            throw new InvalidDataException("Notice outbox exceeds the supported 2 GiB scan limit.");
        var bytes = new byte[(int)input.Length];
        input.ReadExactly(bytes);
        if (bytes[^1] != (byte)'\n')
            throw new InvalidDataException("Notice outbox ends with an incomplete record; refusing to discard evidence.");

        var text = StrictUtf8.GetString(bytes);
        var lines = text.Split('\n');
        var records = new List<NoticeRecord>(Math.Max(0, lines.Length - 1));
        for (var index = 0; index < lines.Length - 1; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
                throw new InvalidDataException("Notice outbox contains an empty journal record.");
            var item = JsonSerializer.Deserialize<NoticeRecord>(lines[index])
                ?? throw new InvalidDataException("Notice outbox contains an empty record.");
            if (string.IsNullOrWhiteSpace(item.NoticeId) ||
                string.IsNullOrWhiteSpace(item.IdempotencyKey) ||
                string.IsNullOrWhiteSpace(item.Payload) ||
                item.PayloadSha256 is null || item.PayloadSha256.Length != 64 ||
                !item.PayloadSha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Notice outbox contains an invalid record.");
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.Payload))).ToLowerInvariant();
            if (!string.Equals(digest, item.PayloadSha256, StringComparison.Ordinal))
                throw new InvalidDataException("Notice outbox payload digest validation failed.");
            var notice = JsonSerializer.Deserialize<ArtifactReadyNotice>(item.Payload)
                ?? throw new InvalidDataException("Notice outbox payload could not be rehydrated.");
            if (!string.Equals(notice.NoticeId, item.NoticeId, StringComparison.Ordinal) ||
                !string.Equals(notice.Manifest.IdempotencyKey, item.IdempotencyKey, StringComparison.Ordinal))
                throw new InvalidDataException("Notice outbox record does not match its notice payload.");
            records.Add(item);
        }
        if (records.Select(item => item.NoticeId).Distinct(StringComparer.Ordinal).Count() != records.Count ||
            records.Select(item => item.IdempotencyKey).Distinct(StringComparer.Ordinal).Count() != records.Count)
            throw new InvalidDataException("Notice outbox contains duplicate notice or idempotency identities.");
        return records.ToArray();
    }

    private void AppendRecord(NoticeRecord record)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record) + "\n");
        using var output = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
        output.Write(bytes);
        output.Flush(flushToDisk: true);
    }

    private sealed record NoticeRecord(
        string NoticeId,
        string IdempotencyKey,
        string Payload,
        string PayloadSha256);
}

public enum FoundryArtifactIngressReceiptRecordOutcome
{
    Recorded,
    Duplicate,
    Conflict
}

/// <summary>
/// Durable local acknowledgement ledger for Foundry's accepted/duplicate ingress receipts.
/// It verifies correlation to the exact persisted notice; it does not cryptographically authenticate
/// a response. Callers must record only receipts returned by the authenticated HTTPS sender.
/// </summary>
public sealed class FileFoundryArtifactIngressReceiptLedger
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly string _path;

    public FileFoundryArtifactIngressReceiptLedger(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public FoundryArtifactIngressReceiptRecordOutcome Record(
        FoundryArtifactIngressReceipt receipt,
        FoundryArtifactReadyNoticeDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(delivery);
        if (!string.Equals(receipt.Contract, FoundryArtifactReadyNoticeClient.Contract, StringComparison.Ordinal) ||
            !string.Equals(receipt.NoticeId, delivery.NoticeId, StringComparison.Ordinal) ||
            !string.Equals(receipt.IdempotencyKey, delivery.IdempotencyKey, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(receipt.ReceiptId) || receipt.ReceiptId.Length > 200 ||
            receipt.ReceiptId.Any(char.IsControl) ||
            receipt.Outcome is not ("accepted" or "duplicate"))
            throw new ArgumentException("Foundry receipt does not match the exact durable notice delivery.", nameof(receipt));

        using var fileLock = AcquireFileLock();
        var records = ReadRecords();
        var existingByReceipt = records.FirstOrDefault(item =>
            string.Equals(item.ReceiptId, receipt.ReceiptId, StringComparison.Ordinal));
        if (existingByReceipt is not null)
            return IsSameReceiptIdentity(existingByReceipt, receipt, delivery)
                ? FoundryArtifactIngressReceiptRecordOutcome.Duplicate
                : FoundryArtifactIngressReceiptRecordOutcome.Conflict;

        var existingByNotice = records.FirstOrDefault(item =>
            string.Equals(item.NoticeId, receipt.NoticeId, StringComparison.Ordinal));
        if (existingByNotice is not null)
            return IsSameReceiptIdentity(existingByNotice, receipt, delivery)
                ? FoundryArtifactIngressReceiptRecordOutcome.Duplicate
                : FoundryArtifactIngressReceiptRecordOutcome.Conflict;

        var json = JsonSerializer.Serialize(receipt);
        AppendRecord(new ReceiptRecord(receipt.ReceiptId, receipt.NoticeId,
            receipt.IdempotencyKey, delivery.PayloadSha256, json));
        return FoundryArtifactIngressReceiptRecordOutcome.Recorded;
    }

    public IReadOnlySet<string> ReadAcknowledgedNoticeIds()
    {
        using var fileLock = AcquireFileLock();
        return new HashSet<string>(ReadRecords().Select(item => item.NoticeId), StringComparer.Ordinal);
    }

    public IReadOnlyList<FoundryArtifactIngressReceipt> ReadReceipts()
    {
        using var fileLock = AcquireFileLock();
        return Array.AsReadOnly(ReadRecords().Select(item =>
            JsonSerializer.Deserialize<FoundryArtifactIngressReceipt>(item.ReceiptJson)
            ?? throw new InvalidDataException("Foundry receipt ledger contains an empty receipt.")).ToArray());
    }

    private static bool IsSameReceiptIdentity(
        ReceiptRecord existing,
        FoundryArtifactIngressReceipt receipt,
        FoundryArtifactReadyNoticeDelivery delivery)
    {
        if (!string.Equals(existing.ReceiptId, receipt.ReceiptId, StringComparison.Ordinal) ||
            !string.Equals(existing.NoticeId, receipt.NoticeId, StringComparison.Ordinal) ||
            !string.Equals(existing.IdempotencyKey, receipt.IdempotencyKey, StringComparison.Ordinal) ||
            !string.Equals(existing.NoticeSha256, delivery.PayloadSha256, StringComparison.Ordinal))
            return false;
        var original = JsonSerializer.Deserialize<FoundryArtifactIngressReceipt>(existing.ReceiptJson);
        return original is not null && original.AcceptedAt == receipt.AcceptedAt;
    }

    private FileStream AcquireFileLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        return new FileStream(_path + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
    }

    private ReceiptRecord[] ReadRecords()
    {
        if (!File.Exists(_path)) return Array.Empty<ReceiptRecord>();
        using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (input.Length == 0) return Array.Empty<ReceiptRecord>();
        if (input.Length > int.MaxValue)
            throw new InvalidDataException("Foundry receipt ledger exceeds the supported 2 GiB scan limit.");
        var bytes = new byte[(int)input.Length];
        input.ReadExactly(bytes);
        if (bytes[^1] != (byte)'\n')
            throw new InvalidDataException("Foundry receipt ledger ends with an incomplete record; refusing to discard receipts.");

        var lines = StrictUtf8.GetString(bytes).Split('\n');
        var records = new List<ReceiptRecord>(Math.Max(0, lines.Length - 1));
        for (var index = 0; index < lines.Length - 1; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
                throw new InvalidDataException("Foundry receipt ledger contains an empty record.");
            var item = JsonSerializer.Deserialize<ReceiptRecord>(lines[index])
                ?? throw new InvalidDataException("Foundry receipt ledger contains an empty record.");
            var receipt = JsonSerializer.Deserialize<FoundryArtifactIngressReceipt>(item.ReceiptJson)
                ?? throw new InvalidDataException("Foundry receipt ledger contains an empty receipt.");
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.ReceiptJson))).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(item.ReceiptId) ||
                string.IsNullOrWhiteSpace(item.NoticeId) ||
                string.IsNullOrWhiteSpace(item.IdempotencyKey) ||
                item.NoticeSha256 is null || item.NoticeSha256.Length != 64 ||
                !item.NoticeSha256.All(Uri.IsHexDigit) ||
                !string.Equals(digest, item.ReceiptJsonSha256, StringComparison.Ordinal) ||
                !string.Equals(item.ReceiptId, receipt.ReceiptId, StringComparison.Ordinal) ||
                !string.Equals(item.NoticeId, receipt.NoticeId, StringComparison.Ordinal) ||
                !string.Equals(item.IdempotencyKey, receipt.IdempotencyKey, StringComparison.Ordinal) ||
                !string.Equals(receipt.Contract, FoundryArtifactReadyNoticeClient.Contract, StringComparison.Ordinal) ||
                receipt.Outcome is not ("accepted" or "duplicate"))
                throw new InvalidDataException("Foundry receipt ledger identity or integrity validation failed.");
            records.Add(item);
        }
        if (records.Select(item => item.ReceiptId).Distinct(StringComparer.Ordinal).Count() != records.Count ||
            records.Select(item => item.NoticeId).Distinct(StringComparer.Ordinal).Count() != records.Count)
            throw new InvalidDataException("Foundry receipt ledger contains duplicate receipt or notice identities.");
        return records.ToArray();
    }

    private void AppendRecord(ReceiptRecord record)
    {
        var complete = record with
        {
            ReceiptJsonSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(record.ReceiptJson))).ToLowerInvariant()
        };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(complete) + "\n");
        using var output = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
        output.Write(bytes);
        output.Flush(flushToDisk: true);
    }

    private sealed record ReceiptRecord(
        string ReceiptId,
        string NoticeId,
        string IdempotencyKey,
        string NoticeSha256,
        string ReceiptJson,
        string ReceiptJsonSha256 = "");
}
