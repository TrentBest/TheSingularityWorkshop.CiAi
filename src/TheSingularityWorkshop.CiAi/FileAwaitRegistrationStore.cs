using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// File-backed immutable Await registration store. An Await ID can be retried identically but
/// cannot be silently retargeted to another consumer, workstream, or set of gates.
/// </summary>
public sealed class FileAwaitRegistrationStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory;

    public FileAwaitRegistrationStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    public AwaitRegistrationStoreOutcome Save(AwaitRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        Directory.CreateDirectory(_directory);
        var path = GetPath(registration.AwaitId);
        using var fileLock = new FileStream(path + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(path))
        {
            var existing = Read(path);
            return AreEquivalent(existing, registration)
                ? AwaitRegistrationStoreOutcome.AlreadyExists
                : AwaitRegistrationStoreOutcome.Conflict;
        }

        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, StoredRegistration.From(registration), JsonOptions);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path);
            return AwaitRegistrationStoreOutcome.Saved;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public AwaitRegistration? Load(string awaitId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(awaitId);
        var path = GetPath(awaitId);
        if (!File.Exists(path)) return null;
        using var fileLock = new FileStream(path + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        return Read(path);
    }

    public IReadOnlyList<AwaitRegistration> ReadAll()
    {
        if (!Directory.Exists(_directory)) return Array.Empty<AwaitRegistration>();
        var registrations = new List<AwaitRegistration>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json").OrderBy(value => value, StringComparer.Ordinal))
        {
            using var fileLock = new FileStream(path + ".lock", FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            registrations.Add(Read(path));
        }
        return Array.AsReadOnly(registrations.OrderBy(item => item.AwaitId, StringComparer.Ordinal).ToArray());
    }

    private string GetPath(string awaitId) =>
        Path.Combine(_directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(awaitId))) + ".json");

    private static AwaitRegistration Read(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var stored = JsonSerializer.Deserialize<StoredRegistration>(input)
            ?? throw new InvalidDataException("Await registration snapshot is empty.");
        if (stored.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException("Await registration schema version is unsupported.");
        if (stored.RequiredGates is null || stored.RequiredGates.Any(item => item is null))
            throw new InvalidDataException("Await registration snapshot is incomplete.");
        try
        {
            return new AwaitRegistration(stored.AwaitId, stored.ConsumerRepository, stored.WorkstreamId,
                stored.RequiredGates.Select(gate => new AwaitGateReference(
                    gate.GateId, gate.Version, gate.ContractDigest, gate.ProviderRepository)).ToArray());
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Await registration snapshot violates its contract.", exception);
        }
    }

    private static bool AreEquivalent(AwaitRegistration left, AwaitRegistration right) =>
        string.Equals(left.AwaitId, right.AwaitId, StringComparison.Ordinal) &&
        string.Equals(left.ConsumerRepository, right.ConsumerRepository, StringComparison.Ordinal) &&
        string.Equals(left.WorkstreamId, right.WorkstreamId, StringComparison.Ordinal) &&
        left.RequiredGates.SequenceEqual(right.RequiredGates);

    private sealed record StoredRegistration(int SchemaVersion, string AwaitId,
        string ConsumerRepository, string WorkstreamId, StoredGateReference[] RequiredGates)
    {
        public static StoredRegistration From(AwaitRegistration item) => new(1, item.AwaitId,
            item.ConsumerRepository, item.WorkstreamId,
            item.RequiredGates.Select(gate => new StoredGateReference(
                gate.GateId, gate.Version, gate.ContractDigest, gate.ProviderRepository)).ToArray());
    }

    private sealed record StoredGateReference(string GateId, int Version, string ContractDigest, string ProviderRepository);
}

/// <summary>Result of persisting an immutable Await registration.</summary>
public enum AwaitRegistrationStoreOutcome
{
    Saved = 0,
    AlreadyExists = 1,
    Conflict = 2
}
