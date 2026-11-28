using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Durable receiver-side store for the latest complete dependency snapshot from each source.
/// Callers must authenticate the source and verify the exact source commit before applying.
/// </summary>
public sealed class FileCiAiDependencyRegistrationLedgerStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory;

    public FileCiAiDependencyRegistrationLedgerStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    /// <summary>
    /// Applies and persists a complete snapshot after external source authentication and exact-SHA
    /// verification. The supplied revision must be a trusted monotonic source sequence, not a Git SHA.
    /// </summary>
    public CiAiDependencyRegistrationApplyResult ApplyVerifiedSnapshot(
        CiAiDependencyRegistrationSnapshot snapshot, long sourceRevision)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var path = GetPath(snapshot.SourceRepository);
        Directory.CreateDirectory(_directory);
        using var fileLock = new FileStream(path + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);

        var current = File.Exists(path) ? Read(path) : null;
        var result = CiAiDependencyRegistrationLedger.ApplyVerifiedSnapshot(current, snapshot, sourceRevision);
        if (result.Status != CiAiDependencyRegistrationApplyStatus.Applied)
            return result;

        var payload = snapshot.ToJson();
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        var stored = new StoredLedger(CurrentSchemaVersion, snapshot.SourceRepository,
            sourceRevision, snapshot.SourceCommitSha, payload, digest);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, stored, JsonOptions);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }

        return result;
    }

    /// <summary>Loads the accepted complete snapshot for one source, or null when none is recorded.</summary>
    public CiAiDependencyRegistrationLedger? Load(string sourceRepository)
    {
        var path = GetPath(sourceRepository);
        if (!File.Exists(path)) return null;
        using var fileLock = new FileStream(path + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        return Read(path);
    }

    /// <summary>Loads all source ledgers in deterministic source-repository order.</summary>
    public IReadOnlyList<CiAiDependencyRegistrationLedger> ReadAll()
    {
        if (!Directory.Exists(_directory)) return Array.Empty<CiAiDependencyRegistrationLedger>();
        var ledgers = new List<CiAiDependencyRegistrationLedger>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json").OrderBy(value => value, StringComparer.Ordinal))
        {
            using var fileLock = new FileStream(path + ".lock", FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            ledgers.Add(Read(path));
        }
        return Array.AsReadOnly(ledgers.OrderBy(item => item.SourceRepository, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    private string GetPath(string sourceRepository)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRepository);
        if (!RepositoryIdentity.IsValid(sourceRepository))
            throw new ArgumentException("Source repository must be canonical owner/repository.", nameof(sourceRepository));
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceRepository.ToUpperInvariant())));
        return Path.Combine(_directory, key + ".json");
    }

    private static CiAiDependencyRegistrationLedger Read(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var stored = JsonSerializer.Deserialize<StoredLedger>(input)
            ?? throw new InvalidDataException("Dependency registration ledger is empty.");
        if (stored.SchemaVersion != CurrentSchemaVersion ||
            string.IsNullOrWhiteSpace(stored.SourceRepository) ||
            stored.SourceRevision < 1 ||
            stored.SourceCommitSha is not { Length: 40 } ||
            !stored.SourceCommitSha.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(stored.SnapshotJson) ||
            stored.SnapshotSha256 is not { Length: 64 } ||
            !stored.SnapshotSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Dependency registration ledger envelope is invalid.");

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stored.SnapshotJson))).ToLowerInvariant();
        if (!string.Equals(digest, stored.SnapshotSha256, StringComparison.Ordinal))
            throw new InvalidDataException("Dependency registration ledger snapshot digest is invalid.");

        try
        {
            var snapshot = CiAiDependencyRegistrationSnapshot.FromJson(stored.SnapshotJson);
            if (!string.Equals(snapshot.SourceRepository, stored.SourceRepository, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(snapshot.SourceCommitSha, stored.SourceCommitSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Dependency registration ledger identity does not match its snapshot.");
            var result = CiAiDependencyRegistrationLedger.ApplyVerifiedSnapshot(null, snapshot, stored.SourceRevision);
            return result.Ledger ?? throw new InvalidDataException("Dependency registration ledger could not be reconstructed.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Dependency registration ledger snapshot is malformed.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Dependency registration ledger snapshot violates its contract.", exception);
        }
    }

    private sealed record StoredLedger(int SchemaVersion, string SourceRepository, long SourceRevision,
        string SourceCommitSha, string SnapshotJson, string SnapshotSha256);
}
