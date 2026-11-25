using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Parsed, immutable repository manifest. It contains declarations only; it grants no CI, network,
/// LLM, deployment, or repository-write authority.
/// </summary>
public sealed record CiAiRepositoryManifest
{
    public const string CurrentSchema = "ciai.repository.v1";

    private CiAiRepositoryManifest(CiAiRepositoryDefinition definition, string? observerProfile)
    {
        Definition = definition;
        ObserverProfile = observerProfile;
    }

    public CiAiRepositoryDefinition Definition { get; }
    public string? ObserverProfile { get; }

    public static CiAiRepositoryManifest ParseJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        RequireObject(root, "manifest");
        RejectDuplicateProperties(root);
        EnsureAllowedProperties(root, "schema", "repository", "observerProfile", "badges", "dependencies", "extensions");
        if (RequiredString(root, "schema") != CurrentSchema)
            throw new InvalidDataException($"Repository manifest must use schema '{CurrentSchema}'.");

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

        var profile = OptionalString(root, "observerProfile");
        if (profile is not null)
            _ = CiWorkflowObserverProfile.Resolve(profile);

        return new CiAiRepositoryManifest(builder.Build(), profile);
    }

    public string ToJson()
    {
        var definition = Definition;
        if (definition.Dependencies.Any(item => item is not CiAiRepositoryDependency))
            throw new InvalidOperationException("Manifest v1 serializes direct repository dependencies only; fallback expressions require a later schema version.");
        var payload = new
        {
            schema = CurrentSchema,
            repository = definition.Repository,
            observerProfile = ObserverProfile,
            badges = definition.Badges.OrderBy(x => x.Id, StringComparer.Ordinal).Select(x => new
            {
                id = x.Id, label = x.Label, workflowFile = x.WorkflowFile
            }),
            dependencies = definition.Dependencies.SelectMany(item => item switch
            {
                CiAiRepositoryDependency direct => new[]
                {
                    new DependencyJson(direct.Repository, direct.Capability)
                },
                _ => Array.Empty<DependencyJson>()
            }).OrderBy(x => x.repository, StringComparer.Ordinal).ThenBy(x => x.capability, StringComparer.Ordinal),
            extensions = definition.Extensions.OrderBy(x => x.Id, StringComparer.Ordinal)
                .Select(x => new { id = x.Id, version = x.Version })
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    private sealed record DependencyJson(string repository, string? capability);

    private static JsonElement RequiredArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Repository manifest requires a '{name}' array.");
        return value;
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
