using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Parsed, immutable repository manifest. It contains declarations only; it grants no CI, network,
/// LLM, deployment, or repository-write authority.
/// </summary>
public sealed record CiAiRepositoryManifest
{
    public const string CurrentSchema = "ciai.repository.v1";
    public const string CurrentSchemaV2 = "ciai.repository.v2";

    private CiAiRepositoryManifest(CiAiRepositoryDefinition definition, string? observerProfile, string schema)
    {
        Definition = definition;
        ObserverProfile = observerProfile;
        Schema = schema;
    }

    public CiAiRepositoryDefinition Definition { get; }
    public string? ObserverProfile { get; }
    public string Schema { get; }

    public static CiAiRepositoryManifest ParseJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        RequireObject(root, "manifest");
        RejectDuplicateProperties(root);
        var schema = RequiredString(root, "schema");
        var isV2 = string.Equals(schema, CurrentSchemaV2, StringComparison.Ordinal);
        if (!isV2 && !string.Equals(schema, CurrentSchema, StringComparison.Ordinal))
            throw new InvalidDataException($"Repository manifest schema '{schema}' is not supported.");
        if (isV2)
            EnsureAllowedProperties(root, "schema", "repository", "observerProfile", "badges", "dependencies", "extensions", "relationships", "adp");
        else
            EnsureAllowedProperties(root, "schema", "repository", "observerProfile", "badges", "dependencies", "extensions");

        var repository = RequiredString(root, "repository");
        var builder = CiAi.ForRepository(repository);

        foreach (var badge in RequiredArray(root, "badges").EnumerateArray())
        {
            RequireObject(badge, "badge");
            EnsureAllowedProperties(badge, "id", "label", "workflowFile");
            builder = builder.WithBadge(new CiAiBadgeDefinition(
                RequiredString(badge, "id"), RequiredString(badge, "label"), RequiredString(badge, "workflowFile")));
        }

        foreach (var dependency in RequiredArray(root, "dependencies").EnumerateArray())
        {
            RequireObject(dependency, "dependency");
            EnsureAllowedProperties(dependency, "repository", "capability");
            var capability = OptionalString(dependency, "capability");
            builder = builder.WithDependency(CiAi.Resolve(RequiredString(dependency, "repository"), capability));
        }

        foreach (var extension in RequiredArray(root, "extensions").EnumerateArray())
        {
            RequireObject(extension, "extension");
            EnsureAllowedProperties(extension, "id", "version");
            builder = builder.ExtendWith(new CiAiExtensionDefinition(
                RequiredString(extension, "id"), RequiredString(extension, "version")));
        }

        if (isV2)
        {
            foreach (var relationship in RequiredArray(root, "relationships").EnumerateArray())
            {
                RequireObject(relationship, "relationship");
                EnsureAllowedProperties(relationship, "relationshipId", "contractVersion", "kind", "sourceRepository", "targetRepository", "artifactKind", "authorityRepository");
                var kindText = RequiredString(relationship, "kind");
                if (!Enum.TryParse<CiRepositoryRelationshipKind>(kindText, ignoreCase: false, out var kind) || !Enum.IsDefined(kind))
                    throw new InvalidDataException($"Unknown ADP relationship kind '{kindText}'.");
                builder = builder.WithRelationship(new CiRepositoryRelationship(
                    RequiredString(relationship, "relationshipId"),
                    RequiredInt32(relationship, "contractVersion"),
                    kind,
                    RequiredString(relationship, "sourceRepository"),
                    RequiredString(relationship, "targetRepository"),
                    RequiredString(relationship, "artifactKind"),
                    RequiredString(relationship, "authorityRepository")));
            }

            if (root.TryGetProperty("adp", out var adp) && adp.ValueKind != JsonValueKind.Null)
            {
                RequireObject(adp, "ADP settings");
                EnsureAllowedProperties(adp, "maximumRepairAttempts", "requireHumanApprovalForWrites");
                builder = builder.WithADP(new CiAiAdpSettings(
                    RequiredInt32(adp, "maximumRepairAttempts"),
                    RequiredBoolean(adp, "requireHumanApprovalForWrites")));
            }
        }

        var profile = OptionalString(root, "observerProfile");
        if (profile is not null)
            _ = CiWorkflowObserverProfile.Resolve(profile);

        return new CiAiRepositoryManifest(builder.Build(), profile, schema);
    }

    public string ToJson()
    {
        var definition = Definition;
        if (definition.Dependencies.Any(item => item is not CiAiRepositoryDependency))
            throw new InvalidOperationException("Repository manifests serialize direct repository dependencies only; fallback expressions require a later schema version.");
        if (definition.LlmAllocation is not null)
            throw new InvalidOperationException("LLM allocation is not yet part of the repository manifest contract.");
        if (Schema == CurrentSchema && (definition.Relationships.Count > 0 || definition.AdpSettings is not null))
            throw new InvalidOperationException("Repository manifest v1 cannot serialize ADP relationships or settings; use schema v2.");
        if (Schema is not (CurrentSchema or CurrentSchemaV2))
            throw new InvalidOperationException($"Unsupported repository manifest schema '{Schema}'.");

        var badges = definition.Badges.OrderBy(x => x.Id, StringComparer.Ordinal).Select(x => new
        {
            id = x.Id, label = x.Label, workflowFile = x.WorkflowFile
        }).ToArray();
        var dependencies = definition.Dependencies.SelectMany(item => item switch
        {
            CiAiRepositoryDependency direct => new[]
            {
                new DependencyJson(direct.Repository, direct.Capability)
            },
            _ => Array.Empty<DependencyJson>()
        }).OrderBy(x => x.repository, StringComparer.Ordinal).ThenBy(x => x.capability, StringComparer.Ordinal).ToArray();
        var extensions = definition.Extensions.OrderBy(x => x.Id, StringComparer.Ordinal)
            .Select(x => new { id = x.Id, version = x.Version }).ToArray();

        if (Schema == CurrentSchema)
        {
            var payloadV1 = new
            {
                schema = CurrentSchema,
                repository = definition.Repository,
                observerProfile = ObserverProfile,
                badges,
                dependencies,
                extensions
            };
            return JsonSerializer.Serialize(payloadV1, new JsonSerializerOptions { WriteIndented = true }) + "\n";
        }

        var relationships = definition.Relationships.OrderBy(x => x.RelationshipId, StringComparer.Ordinal)
            .Select(x => new
            {
                relationshipId = x.RelationshipId,
                contractVersion = x.ContractVersion,
                kind = x.Kind.ToString(),
                sourceRepository = x.SourceRepository,
                targetRepository = x.TargetRepository,
                artifactKind = x.ArtifactKind,
                authorityRepository = x.AuthorityRepository
            }).ToArray();
        var adp = definition.AdpSettings is null ? null : new
        {
            maximumRepairAttempts = definition.AdpSettings.MaximumRepairAttempts,
            requireHumanApprovalForWrites = definition.AdpSettings.RequireHumanApprovalForWrites
        };
        var payloadV2 = new
        {
            schema = CurrentSchemaV2,
            repository = definition.Repository,
            observerProfile = ObserverProfile,
            badges,
            dependencies,
            extensions,
            relationships,
            adp
        };
        return JsonSerializer.Serialize(payloadV2, new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    private sealed record DependencyJson(string repository, string? capability);

    private static JsonElement RequiredArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Repository manifest requires a '{name}' array.");
        return value;
    }

    private static int RequiredInt32(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var result))
            throw new InvalidDataException($"Repository manifest requires an integer '{name}'.");
        return result;
    }

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException($"Repository manifest requires a boolean '{name}'.");
        return value.GetBoolean();
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        var value = OptionalString(parent, name);
        return value ?? throw new InvalidDataException($"Repository manifest requires a non-empty '{name}' string.");
    }

    private static string? OptionalString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"Repository manifest property '{name}' must be a non-empty string or null.");
        return value.GetString();
    }

    private static void RequireObject(JsonElement value, string label)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Repository manifest {label} must be an object.");
    }

    private static void EnsureAllowedProperties(JsonElement value, params string[] allowed)
    {
        var set = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!set.Contains(property.Name))
                throw new InvalidDataException($"Unsupported repository manifest property '{property.Name}'.");
    }

    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException($"Repository manifest contains duplicate property '{property.Name}'.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) RejectDuplicateProperties(item);
        }
    }
}
