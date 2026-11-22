using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

public sealed partial record CiAiRepositoryConfiguration
{
    /// <summary>Parses a declarative ciai.repository.v1 manifest. Unknown executable content is never accepted.</summary>
    public static CiAiRepositoryConfiguration ParseJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !string.Equals(RequiredString(root, "schema"), "ciai.repository.v1", StringComparison.Ordinal))
            throw new InvalidDataException("Repository manifest must use schema 'ciai.repository.v1'.");

        var repository = RequiredString(root, "repository");
        var builder = new CiAiRepositoryBuilder();

        if (!root.TryGetProperty("badges", out var badges) || badges.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Repository manifest must include a badges array.");
        foreach (var badge in badges.EnumerateArray())
            builder = builder.WithBadge(new CiAiBadgeDefinition(
                RequiredString(badge, "id"),
                RequiredString(badge, "label"),
                RequiredString(badge, "workflowFile")));

        if (!root.TryGetProperty("dependencies", out var dependencies) || dependencies.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Repository manifest must include a dependencies array.");
        foreach (var dependency in dependencies.EnumerateArray())
        {
            var capability = dependency.TryGetProperty("capability", out var capabilityValue) &&
                capabilityValue.ValueKind == JsonValueKind.String ? capabilityValue.GetString() : null;
            builder = builder.WithDependency(new CiAiDependencyReference(
                RequiredString(dependency, "repository"), capability));
        }

        if (!root.TryGetProperty("extensions", out var extensions) || extensions.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Repository manifest must include an extensions array.");
        foreach (var extension in extensions.EnumerateArray())
            builder = builder.ExtendWith(new CiAiExtensionDefinition(
                RequiredString(extension, "id"), RequiredString(extension, "version")));

        return builder.Build(repository);
    }

    private static string RequiredString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"Repository manifest is missing a non-empty '{name}' string.");
        return value.GetString()!;
    }
}
