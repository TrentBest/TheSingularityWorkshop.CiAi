using System.Security.Cryptography;
using System.Text;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// A complete, source-owned snapshot of the dependencies declared by one repository at one exact commit.
/// It is an export artifact for an explicit registration/synchronization stage; creating it performs no I/O.
/// </summary>
public sealed record CiAiDependencyRegistrationSnapshot
{
    public const string CurrentSchema = "ciai.dependency-registration.v1";

    private CiAiDependencyRegistrationSnapshot(
        string sourceRepository,
        string sourceCommitSha,
        IReadOnlyList<CiAiDependencyRegistration> registrations)
    {
        SourceRepository = sourceRepository;
        SourceCommitSha = sourceCommitSha;
        Registrations = Array.AsReadOnly(registrations.ToArray());
        Validate();
    }

    public string SchemaVersion => CurrentSchema;
    public string SourceRepository { get; }
    public string SourceCommitSha { get; }

    /// <summary>
    /// Complete set of outgoing dependency declarations at SourceCommitSha.
    /// An empty set is meaningful: it declares that this source has no dependencies at this revision.
    /// </summary>
    public IReadOnlyList<CiAiDependencyRegistration> Registrations { get; }

    /// <summary>
    /// Creates a deterministic registration snapshot from the effective immutable declaration.
    /// The caller must supply the exact 40-character source commit SHA.
    /// </summary>
    public static CiAiDependencyRegistrationSnapshot FromDefinition(
        CiAiRepositoryDefinition definition,
        string sourceCommitSha)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!IsCommitSha(sourceCommitSha))
            throw new ArgumentException("Source commit must be a full 40-character hexadecimal SHA.", nameof(sourceCommitSha));

        var registrations = new List<CiAiDependencyRegistration>();
        for (var declarationIndex = 0; declarationIndex < definition.Dependencies.Count; declarationIndex++)
        {
            switch (definition.Dependencies[declarationIndex])
            {
                case CiAiRepositoryDependency direct:
                    registrations.Add(Create(direct.Repository, direct.Capability, "required", declarationIndex, 0));
                    break;

                case CiAiOrderedDependencyFallback fallback:
                    for (var candidateIndex = 0; candidateIndex < fallback.Repositories.Count; candidateIndex++)
                        registrations.Add(Create(fallback.Repositories[candidateIndex], null,
                            "fallback-candidate", declarationIndex, candidateIndex));
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported dependency expression type '{definition.Dependencies[declarationIndex].GetType().FullName}'.");
            }
        }

        return new CiAiDependencyRegistrationSnapshot(
            definition.Repository,
            sourceCommitSha.ToLowerInvariant(),
            registrations.OrderBy(x => x.DeclarationIndex)
                .ThenBy(x => x.CandidateIndex)
                .ThenBy(x => x.RegistrationId, StringComparer.Ordinal)
                .ToArray());

        CiAiDependencyRegistration Create(
            string targetRepository, string? capability, string kind, int declarationIndex, int candidateIndex)
        {
            if (!RepositoryIdentity.IsValid(targetRepository))
                throw new InvalidOperationException($"Dependency target '{targetRepository}' is not canonical.");
            var identity = string.Join("|", definition.Repository.ToUpperInvariant(),
                targetRepository.ToUpperInvariant(), capability ?? string.Empty, kind,
                declarationIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                candidateIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var id = "depreg-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))
                .AsSpan(0, 16)).ToLowerInvariant();
            return new CiAiDependencyRegistration(id, definition.Repository, targetRepository,
                capability, kind, declarationIndex, candidateIndex);
        }
    }

    private void Validate()
    {
        if (!RepositoryIdentity.IsValid(SourceRepository))
            throw new ArgumentException("Registration source must use canonical owner/repository identity.");
        if (!IsCommitSha(SourceCommitSha))
            throw new ArgumentException("Registration source commit must be a full 40-character hexadecimal SHA.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var registration in Registrations)
        {
            if (!ids.Add(registration.RegistrationId))
                throw new ArgumentException($"Duplicate dependency registration '{registration.RegistrationId}'.");
            if (!string.Equals(registration.SourceRepository, SourceRepository, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Every registration must belong to the snapshot source repository.");
            if (!RepositoryIdentity.IsValid(registration.TargetRepository) ||
                string.Equals(registration.TargetRepository, SourceRepository, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Registration targets must be canonical and cannot be the source repository.");
            if (registration.DependencyKind is not ("required" or "fallback-candidate") ||
                registration.DeclarationIndex < 0 || registration.CandidateIndex < 0)
                throw new ArgumentException("Dependency registration metadata is invalid.");
        }
    }

    private static bool IsCommitSha(string? value) =>
        value is { Length: 40 } && value.All(Uri.IsHexDigit);
}

/// <summary>
/// Derived inbound-registration payload. It records who declared a dependency on a target;
/// it does not become or modify the target repository's own dependency declaration.
/// </summary>
public sealed record CiAiDependencyRegistration(
    string RegistrationId,
    string SourceRepository,
    string TargetRepository,
    string? Capability,
    string DependencyKind,
    int DeclarationIndex,
    int CandidateIndex);
