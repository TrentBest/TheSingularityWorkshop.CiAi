using System.Security.Cryptography;
using System.Text;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Describes a directed cross-repository contract edge. A repository may be both producer and consumer;
/// those roles are represented by separate edges rather than inferred from a package dependency.
/// </summary>
public enum CiRepositoryRelationshipKind
{
    ContentContribution = 0,
    CanonicalContentConsumption = 1,
    ExperienceValidation = 2,
    BuildDependency = 3
}

/// <summary>Immutable declaration of one directed repository relationship.</summary>
public sealed record CiRepositoryRelationship
{
    public CiRepositoryRelationship(
        string relationshipId,
        int contractVersion,
        CiRepositoryRelationshipKind kind,
        string sourceRepository,
        string targetRepository,
        string artifactKind,
        string authorityRepository)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRepository);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRepository);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorityRepository);

        if (!StableId.IsMatch(relationshipId))
            throw new ArgumentException("Relationship ID must be a stable lowercase kebab-case identifier.", nameof(relationshipId));
        if (contractVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(contractVersion));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!RepositoryIdentity.IsValid(sourceRepository) ||
            !RepositoryIdentity.IsValid(targetRepository) ||
            !RepositoryIdentity.IsValid(authorityRepository))
            throw new ArgumentException("Relationship endpoints and authority must be canonical owner/repository identities.");
        if (string.Equals(sourceRepository, targetRepository, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Cross-repository relationships require distinct source and target repositories.");
        if (artifactKind.Length > 100 || artifactKind.Any(char.IsControl))
            throw new ArgumentException("Artifact kind must be a bounded, non-empty identifier.", nameof(artifactKind));

        RelationshipId = relationshipId;
        ContractVersion = contractVersion;
        Kind = kind;
        SourceRepository = sourceRepository;
        TargetRepository = targetRepository;
        ArtifactKind = artifactKind;
        AuthorityRepository = authorityRepository;
    }

    private static readonly System.Text.RegularExpressions.Regex StableId =
        new("^[a-z0-9]+(?:-[a-z0-9]+)*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public string RelationshipId { get; }
    public int ContractVersion { get; }
    public CiRepositoryRelationshipKind Kind { get; }
    public string SourceRepository { get; }
    public string TargetRepository { get; }
    public string ArtifactKind { get; }

    /// <summary>The repository that owns the meaning/admission decision for this edge's artifact.</summary>
    public string AuthorityRepository { get; }
}

/// <summary>
/// Immutable notice that one exact repository revision changed an artifact relevant to a relationship.
/// It is a trigger/evidence identity, not permission to write to either repository.
/// </summary>
public sealed record CiRepositoryChangeNotice
{
    public CiRepositoryChangeNotice(
        string eventId,
        CiRepositoryRelationship relationship,
        string sourceCommitSha,
        string artifactIdentity,
        string artifactSha256,
        string? causationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentNullException.ThrowIfNull(relationship);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceCommitSha);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactIdentity);

        if (!IsFullSha(sourceCommitSha))
            throw new ArgumentException("Source revision must be a full 40-character commit SHA.", nameof(sourceCommitSha));
        if (!IsSha256(artifactSha256))
            throw new ArgumentException("Artifact digest must be a full SHA-256 hexadecimal value.", nameof(artifactSha256));
        if (artifactIdentity.Length > 300 || artifactIdentity.Any(char.IsControl))
            throw new ArgumentException("Artifact identity must be bounded and contain no control characters.", nameof(artifactIdentity));
        if (causationId is not null && (causationId.Length is 0 or > 200 || causationId.Any(char.IsControl)))
            throw new ArgumentException("Causation ID must be bounded and contain no control characters.", nameof(causationId));

        EventId = eventId;
        Relationship = relationship;
        SourceCommitSha = sourceCommitSha.ToLowerInvariant();
        ArtifactIdentity = artifactIdentity;
        ArtifactSha256 = artifactSha256.ToLowerInvariant();
        CausationId = causationId;
        IdempotencyKey = ComputeIdempotencyKey(relationship, SourceCommitSha, artifactIdentity, ArtifactSha256);
    }

    public string EventId { get; }
    public CiRepositoryRelationship Relationship { get; }
    public string SourceCommitSha { get; }
    public string ArtifactIdentity { get; }
    public string ArtifactSha256 { get; }
    public string? CausationId { get; }

    /// <summary>
    /// Stable across delivery retries for the same relationship, source revision, and artifact bytes.
    /// A changed revision or artifact produces a new key and therefore new validation work.
    /// </summary>
    public string IdempotencyKey { get; }

    private static bool IsFullSha(string value) =>
        value.Length == 40 && value.All(Uri.IsHexDigit);

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string ComputeIdempotencyKey(
        CiRepositoryRelationship relationship, string sourceSha, string artifactIdentity, string digest)
    {
        var canonical = string.Join("\n",
            relationship.RelationshipId,
            relationship.ContractVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            relationship.Kind.ToString(),
            relationship.SourceRepository.ToLowerInvariant(),
            relationship.TargetRepository.ToLowerInvariant(),
            sourceSha,
            artifactIdentity,
            digest);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
