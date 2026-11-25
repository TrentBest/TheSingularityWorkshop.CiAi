using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// File-backed immutable gate contract registry. A provider/gate/version identity cannot be
/// silently rebound to a different approved contract digest.
/// </summary>
public sealed class FileAwaitGateContractStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory;

    public FileAwaitGateContractStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    public AwaitGateContractStoreOutcome Save(AwaitGateContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        Directory.CreateDirectory(_directory);
        var path = GetPath(contract.ProviderRepository, contract.GateId, contract.Version);
        using var fileLock = new FileStream(path + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(path))
        {
            var existing = Read(path);
            return string.Equals(existing.ContractDigest, contract.ContractDigest, StringComparison.Ordinal)
                ? AwaitGateContractStoreOutcome.AlreadyExists
                : AwaitGateContractStoreOutcome.Conflict;
        }

        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, StoredContract.From(contract), JsonOptions);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path);
            return AwaitGateContractStoreOutcome.Saved;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public AwaitGateContract? Load(string providerRepository, string gateId, int version)
    {
        var path = GetPath(providerRepository, gateId, version);
        if (!File.Exists(path)) return null;
        using var fileLock = new FileStream(path + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        return Read(path);
    }

    public IReadOnlyList<AwaitGateContract> ReadAll()
    {
        if (!Directory.Exists(_directory)) return Array.Empty<AwaitGateContract>();
        var contracts = new List<AwaitGateContract>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json").OrderBy(value => value, StringComparer.Ordinal))
        {
            using var fileLock = new FileStream(path + ".lock", FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            contracts.Add(Read(path));
        }
        return Array.AsReadOnly(contracts
            .OrderBy(item => item.ProviderRepository, StringComparer.Ordinal)
            .ThenBy(item => item.GateId, StringComparer.Ordinal)
            .ThenBy(item => item.Version)
            .ToArray());
    }

    private string GetPath(string providerRepository, string gateId, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerRepository);
        ArgumentException.ThrowIfNullOrWhiteSpace(gateId);
        if (!RepositoryIdentity.IsValid(providerRepository))
            throw new ArgumentException("Repository must be canonical owner/repository.", nameof(providerRepository));
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        var identity = $"{providerRepository}:{gateId}:v{version}";
        return Path.Combine(_directory,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))) + ".json");
    }

    private static AwaitGateContract Read(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var stored = JsonSerializer.Deserialize<StoredContract>(input)
            ?? throw new InvalidDataException("Await gate contract snapshot is empty.");
        if (stored.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException("Await gate contract schema version is unsupported.");
        if (stored.Approval is null || stored.Requirements is null || stored.Requirements.Any(item => item is null))
            throw new InvalidDataException("Await gate contract snapshot is incomplete.");
        try
        {
            var contract = new AwaitGateContract(stored.GateId, stored.Version, stored.ConsumerRepository,
                stored.ProviderRepository, stored.ProviderSourceSha, stored.ManifestDigest,
                stored.EvaluationPolicyVersion,
                new AwaitGateApproval(stored.Approval.ApprovalReference, stored.Approval.AuthorityPolicyId,
                    stored.Approval.ApproverIdentity, stored.Approval.Kind, stored.Approval.ApprovedAt),
                stored.Requirements.Select(item => new AwaitGateRequirement(item.RequirementId,
                    item.Description, item.ExpectedValue, item.EvidenceKind)).ToArray());
            if (!string.Equals(contract.ContractDigest, stored.ContractDigest, StringComparison.Ordinal))
                throw new InvalidDataException("Stored gate contract digest does not match its canonical contents.");
            return contract;
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Await gate contract snapshot violates its contract.", exception);
        }
    }

    private sealed record StoredContract(int SchemaVersion, string GateId, int Version,
        string ConsumerRepository, string ProviderRepository, string ProviderSourceSha,
        string ManifestDigest, string ContractDigest, string EvaluationPolicyVersion,
        StoredApproval Approval, StoredRequirement[] Requirements)
    {
        public static StoredContract From(AwaitGateContract item) => new(1, item.GateId, item.Version,
            item.ConsumerRepository, item.ProviderRepository, item.ProviderSourceSha, item.ManifestDigest,
            item.ContractDigest, item.EvaluationPolicyVersion,
            new StoredApproval(item.Approval.ApprovalReference, item.Approval.AuthorityPolicyId,
                item.Approval.ApproverIdentity, item.Approval.Kind, item.Approval.ApprovedAt),
            item.Requirements.Select(requirement => new StoredRequirement(requirement.RequirementId,
                requirement.Description, requirement.ExpectedValue, requirement.EvidenceKind)).ToArray());
    }

    private sealed record StoredApproval(string ApprovalReference, string AuthorityPolicyId,
        string ApproverIdentity, AwaitGateApprovalKind Kind, DateTimeOffset ApprovedAt);

    private sealed record StoredRequirement(string RequirementId, string Description,
        string ExpectedValue, AwaitGateEvidenceKind EvidenceKind);
}

/// <summary>Result of persisting a versioned gate contract.</summary>
public enum AwaitGateContractStoreOutcome
{
    Saved = 0,
    AlreadyExists = 1,
    Conflict = 2
}
