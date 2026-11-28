using System.Security.Cryptography;
using System.Text;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// A repository inventory returned by a host-controlled, authenticated inventory provider.
/// Completeness is a trust assertion by that provider; callers must not derive it from a partial search result.
/// </summary>
public sealed class CiAiRepositoryInventorySnapshot
{
    private CiAiRepositoryInventorySnapshot(string providerIdentity, long revision,
        IReadOnlyList<string> repositories, bool isComplete, string evidencePayloadSha256)
    {
        ProviderIdentity = providerIdentity;
        Revision = revision;
        Repositories = repositories;
        IsComplete = isComplete;
        EvidencePayloadSha256 = evidencePayloadSha256;
    }

    public string ProviderIdentity { get; }
    public long Revision { get; }
    public IReadOnlyList<string> Repositories { get; }
    public bool IsComplete { get; }
    public string EvidencePayloadSha256 { get; }

    public static CiAiRepositoryInventorySnapshot CreateFromTrustedProvider(
        string providerIdentity, long revision, IEnumerable<string> repositories,
        bool isComplete, string evidencePayloadSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerIdentity);
        ArgumentNullException.ThrowIfNull(repositories);
        if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
        if (evidencePayloadSha256 is not { Length: 64 } || !evidencePayloadSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Inventory evidence digest must be a full SHA-256 value.", nameof(evidencePayloadSha256));
        var identities = repositories.ToArray();
        if (identities.Any(value => !RepositoryIdentity.IsValid(value)))
            throw new ArgumentException("Inventory contains a non-canonical repository identity.", nameof(repositories));
        if (identities.Distinct(StringComparer.OrdinalIgnoreCase).Count() != identities.Length)
            throw new ArgumentException("Inventory repository identities must be unique.", nameof(repositories));
        return new CiAiRepositoryInventorySnapshot(providerIdentity, revision,
            Array.AsReadOnly(identities.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray()),
            isComplete, evidencePayloadSha256.ToLowerInvariant());
    }
}

/// <summary>
/// Produces a complete fan-out recipient set from a trusted repository inventory and prior deliveries.
/// Prior recipients are retained so removed declarations can be withdrawn even if a repository is no
/// longer present in the current inventory.
/// </summary>
public static class CiAiDependencyRegistrationFanOutPlanner
{
    public static IReadOnlyList<string> CreateRecipients(
        string sourceRepository,
        CiAiRepositoryInventorySnapshot inventory,
        IEnumerable<CiAiDependencyRegistrationDelivery> previousDeliveries)
    {
        if (!RepositoryIdentity.IsValid(sourceRepository))
            throw new ArgumentException("Source repository must be canonical owner/repository.", nameof(sourceRepository));
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(previousDeliveries);
        if (!inventory.IsComplete)
            throw new InvalidDataException("Incomplete repository inventory cannot authorize dependency fan-out.");

        var previous = previousDeliveries.ToArray();
        if (previous.Any(delivery => delivery is null))
            throw new ArgumentException("Previous delivery collection contains a null entry.", nameof(previousDeliveries));

        var recipients = new HashSet<string>(inventory.Repositories, StringComparer.OrdinalIgnoreCase);
        foreach (var delivery in previous.Where(item =>
            string.Equals(item.SourceRepository, sourceRepository, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var recipient in delivery.RecipientRepositories)
                recipients.Add(recipient);
        }

        recipients.RemoveWhere(value => string.Equals(value, sourceRepository, StringComparison.OrdinalIgnoreCase));
        return Array.AsReadOnly(recipients.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray());
    }
}

/// <summary>
/// Authenticated host boundary for obtaining a complete organization/repository inventory.
/// Implementations must paginate to completion and fail closed on inaccessible pages or permission errors.
/// </summary>
public interface ICiAiTrustedRepositoryInventoryProvider
{
    ValueTask<CiAiRepositoryInventorySnapshot> GetCompleteInventoryAsync(
        CancellationToken cancellationToken = default);
}
