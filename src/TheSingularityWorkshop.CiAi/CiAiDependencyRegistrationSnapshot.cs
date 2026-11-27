using System.Security.Cryptography;
using System.Text.Json;
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

    /// <summary>
    /// Parses a registration payload and validates its schema and declaration identities.
    /// Parsing does not authenticate the source or verify commit provenance; callers must do that
    /// independently before passing the snapshot to receiver reconciliation.
    /// </summary>
    public static CiAiDependencyRegistrationSnapshot FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        ValidateObjectProperties(root, "schema", "sourceRepository", "sourceCommitSha", "registrations");
        if (!string.Equals(root.GetProperty("schema").GetString(), CurrentSchema, StringComparison.Ordinal))
            throw new JsonException("Dependency registration schema is missing or unsupported.");

        var sourceRepository = root.GetProperty("sourceRepository").GetString()
            ?? throw new JsonException("Dependency registration source repository is required.");
        var sourceCommitSha = root.GetProperty("sourceCommitSha").GetString()
            ?? throw new JsonException("Dependency registration source SHA is required.");
        var registrationArray = root.GetProperty("registrations");
        if (registrationArray.ValueKind != JsonValueKind.Array)
            throw new JsonException("Dependency registrations must be an array.");

        var registrations = new List<CiAiDependencyRegistration>();
        foreach (var item in registrationArray.EnumerateArray())
        {
            ValidateObjectProperties(item, "registrationId", "sourceRepository", "targetRepository",
                "capability", "dependencyKind", "declarationIndex", "candidateIndex");

            var capabilityElement = item.GetProperty("capability");
            var capability = capabilityElement.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => capabilityElement.GetString(),
                _ => throw new JsonException("Dependency capability must be a string or null.")
            };

            registrations.Add(new CiAiDependencyRegistration(
                item.GetProperty("registrationId").GetString()
                    ?? throw new JsonException("Registration identity is required."),
                item.GetProperty("sourceRepository").GetString()
                    ?? throw new JsonException("Registration source is required."),
                item.GetProperty("targetRepository").GetString()
                    ?? throw new JsonException("Registration target is required."),
                capability,
                item.GetProperty("dependencyKind").GetString()
                    ?? throw new JsonException("Registration kind is required."),
                item.GetProperty("declarationIndex").GetInt32(),
                item.GetProperty("candidateIndex").GetInt32()));
        }

        return new CiAiDependencyRegistrationSnapshot(sourceRepository, sourceCommitSha, registrations);
    }

    /// <summary>Serializes the complete snapshot as deterministic, indented JSON for an explicit transport stage.</summary>
    public string ToJson()
    {
        var payload = new
        {
            schema = CurrentSchema,
            sourceRepository = SourceRepository,
            sourceCommitSha = SourceCommitSha,
            registrations = Registrations.Select(x => new
            {
                registrationId = x.RegistrationId,
                sourceRepository = x.SourceRepository,
                targetRepository = x.TargetRepository,
                capability = x.Capability,
                dependencyKind = x.DependencyKind,
                declarationIndex = x.DeclarationIndex,
                candidateIndex = x.CandidateIndex
            }).ToArray()
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    private static void ValidateObjectProperties(JsonElement element, params string[] expectedProperties)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new JsonException("The dependency registration payload contains a non-object value.");

        var expected = expectedProperties.ToHashSet(StringComparer.Ordinal);
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!expected.Contains(property.Name))
                throw new JsonException($"Unexpected dependency registration property '{property.Name}'.");
            if (!actual.Add(property.Name))
                throw new JsonException($"Duplicate dependency registration property '{property.Name}'.");
        }

        var missing = expected.Except(actual, StringComparer.Ordinal).ToArray();
        if (missing.Length != 0)
            throw new JsonException($"Required dependency registration property '{missing[0]}' is missing.");
    }

    private void Validate()
    {
        if (!RepositoryIdentity.IsValid(SourceRepository))
            throw new ArgumentException("Registration source must use canonical owner/repository identity.");
        if (!IsCommitSha(SourceCommitSha))
            throw new ArgumentException("Registration source commit must be a full 40-character hexadecimal SHA.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var positions = new HashSet<(int DeclarationIndex, int CandidateIndex)>();
        foreach (var registration in Registrations)
        {
            if (!ids.Add(registration.RegistrationId))
                throw new ArgumentException($"Duplicate dependency registration '{registration.RegistrationId}'.");
            if (!string.Equals(registration.SourceRepository, SourceRepository, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Every registration must belong to the snapshot source repository.");
            if (!RepositoryIdentity.IsValid(registration.TargetRepository) ||
                string.Equals(registration.TargetRepository, SourceRepository, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Registration targets must be canonical and cannot be the source repository.");
            if (string.IsNullOrWhiteSpace(registration.RegistrationId) ||
                registration.DependencyKind is not ("required" or "fallback-candidate") ||
                registration.DeclarationIndex < 0 || registration.CandidateIndex < 0 ||
                (registration.DependencyKind == "required" && registration.CandidateIndex != 0) ||
                (registration.DependencyKind == "fallback-candidate" && registration.Capability is not null))
                throw new ArgumentException("Dependency registration metadata is invalid.");

            var expectedId = CreateRegistrationId(SourceRepository, registration.TargetRepository,
                registration.Capability, registration.DependencyKind,
                registration.DeclarationIndex, registration.CandidateIndex);
            if (!string.Equals(registration.RegistrationId, expectedId, StringComparison.Ordinal))
                throw new ArgumentException($"Dependency registration identity '{registration.RegistrationId}' does not match its declaration.");

            if (!positions.Add((registration.DeclarationIndex, registration.CandidateIndex)))
                throw new ArgumentException("Dependency declaration/candidate positions must be unique.");
        }
    }

    private static string CreateRegistrationId(
        string sourceRepository, string targetRepository, string? capability,
        string kind, int declarationIndex, int candidateIndex)
    {
        var identity = string.Join("|", sourceRepository.ToUpperInvariant(),
            targetRepository.ToUpperInvariant(), capability ?? string.Empty, kind,
            declarationIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            candidateIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return "depreg-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))
            .AsSpan(0, 16)).ToLowerInvariant();
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
