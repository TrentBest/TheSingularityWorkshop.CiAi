using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Entry point for declarative repository-level CiAi configuration.</summary>
public static class CiAi
{
    /// <summary>Starts a repository configuration with one enabled badge.</summary>
    public static CiAiRepositoryBuilder WithBadge(CiAiBadgeDefinition badge)
        => new CiAiRepositoryBuilder().WithBadge(badge);

    /// <summary>
    /// Creates a typed dependency reference. A short repository name resolves to the owner of
    /// the repository passed to Build; an owner/repository identity remains explicit.
    /// Resolution is declarative and performs no network or repository mutation.
    /// </summary>
    public static CiAiDependencyReference Resolve(string repository, string? capability = null)
        => new(repository, capability);

    /// <summary>Starts a repository configuration with one dependency declaration.</summary>
    public static CiAiRepositoryBuilder WithDependency(CiAiDependencyReference dependency)
        => new CiAiRepositoryBuilder().WithDependency(dependency);

    /// <summary>Starts a repository configuration with one declarative extension.</summary>
    public static CiAiRepositoryBuilder ExtendWith(CiAiExtensionDefinition extension)
        => new CiAiRepositoryBuilder().ExtendWith(extension);
}

/// <summary>A declarative, validated badge definition. URLs are derived only when a repository is bound.</summary>
public sealed record CiAiBadgeDefinition
{
    public CiAiBadgeDefinition(string id, string label, string workflowFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowFile);
        if (id.Length > 64 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("Badge ID must be a short stable identifier.", nameof(id));
        if (label.Length > 80 || label.Any(char.IsControl))
            throw new ArgumentException("Badge label is invalid.", nameof(label));
        if (workflowFile.Contains('/') || workflowFile.Contains('\\') ||
            !(workflowFile.EndsWith(".yml", StringComparison.Ordinal) || workflowFile.EndsWith(".yaml", StringComparison.Ordinal)) ||
            workflowFile is "." or "..")
            throw new ArgumentException("Badge workflow must be a workflow filename.", nameof(workflowFile));
        Id = id;
        Label = label;
        WorkflowFile = workflowFile;
    }

    public string Id { get; }
    public string Label { get; }
    public string WorkflowFile { get; }

    public string ImageUrl(string repository)
    {
        ValidateRepository(repository);
        return $"https://github.com/{repository}/actions/workflows/{WorkflowFile}/badge.svg";
    }

    public string TargetUrl(string repository)
    {
        ValidateRepository(repository);
        return $"https://github.com/{repository}/actions/workflows/{WorkflowFile}";
    }

    private static void ValidateRepository(string repository)
    {
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must use canonical owner/repository identity.", nameof(repository));
    }
}

/// <summary>Built-in badge definitions. The image is GitHub's live SVG workflow badge, not a hard-coded pass claim.</summary>
public static class Badges
{
    public static CiAiBadgeDefinition SithPolicy() => new("sith-policy", "Sith Policy", "sith-policy.yml");
}

/// <summary>A repository dependency declaration; it does not itself authorize package adoption or release.</summary>
public sealed record CiAiDependencyReference
{
    public CiAiDependencyReference(string repository, string? capability = null)
    {
        if (!RepositoryIdentity.IsValid(repository) && !IsShortRepositoryName(repository))
            throw new ArgumentException("Dependency must be a repository name or canonical owner/repository identity.", nameof(repository));
        if (capability is { Length: > 100 } || capability?.Any(char.IsControl) == true)
            throw new ArgumentException("Dependency capability is invalid.", nameof(capability));
        Repository = repository;
        Capability = string.IsNullOrWhiteSpace(capability) ? null : capability.Trim();
    }

    public string Repository { get; }
    public string? Capability { get; }

    internal CiAiDependencyReference ResolveForOwner(string owner)
        => RepositoryIdentity.IsValid(Repository) ? this : new CiAiDependencyReference($"{owner}/{Repository}", Capability);

    private static bool IsShortRepositoryName(string value)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Length <= 100 &&
           value is not "." and not ".." &&
           value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}

/// <summary>A declarative extension point. Extensions are identifiers and versions, never executable model-authored code.</summary>
public sealed record CiAiExtensionDefinition
{
    public CiAiExtensionDefinition(string id, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (id.Length > 120 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '_'))
            throw new ArgumentException("Extension ID is invalid.", nameof(id));
        if (version.Length > 40 || version.Any(char.IsControl))
            throw new ArgumentException("Extension version is invalid.", nameof(version));
        Id = id;
        Version = version;
    }

    public string Id { get; }
    public string Version { get; }
}

/// <summary>Gauntlet integration declarations.</summary>
public static class Gauntlet
{
    public static CiAiExtensionDefinition Registrar(string version = "1")
        => new("Gauntlet.Registrar", version);
}

/// <summary>Immutable repository-level CiAi configuration.</summary>
public sealed partial record CiAiRepositoryConfiguration
{
    internal CiAiRepositoryConfiguration(
        string repository,
        IReadOnlyList<CiAiBadgeDefinition> badges,
        IReadOnlyList<CiAiDependencyReference> dependencies,
        IReadOnlyList<CiAiExtensionDefinition> extensions)
    {
        Repository = repository;
        Badges = Array.AsReadOnly(badges.ToArray());
        Dependencies = Array.AsReadOnly(dependencies.ToArray());
        Extensions = Array.AsReadOnly(extensions.ToArray());
    }

    public string Repository { get; }
    public IReadOnlyList<CiAiBadgeDefinition> Badges { get; }
    public IReadOnlyList<CiAiDependencyReference> Dependencies { get; }
    public IReadOnlyList<CiAiExtensionDefinition> Extensions { get; }

    /// <summary>Stable JSON representation suitable for a checked-in repository manifest.</summary>
    public string ToJson()
    {
        var value = new
        {
            schema = "ciai.repository.v1",
            repository = Repository,
            badges = Badges.OrderBy(x => x.Id, StringComparer.Ordinal).Select(x => new
            {
                id = x.Id, label = x.Label, workflowFile = x.WorkflowFile
            }),
            dependencies = Dependencies.OrderBy(x => x.Repository, StringComparer.Ordinal)
                .ThenBy(x => x.Capability, StringComparer.Ordinal)
                .Select(x => new { repository = x.Repository, capability = x.Capability }),
            extensions = Extensions.OrderBy(x => x.Id, StringComparer.Ordinal)
                .Select(x => new { id = x.Id, version = x.Version })
        };
        return JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        }) + "\n";
    }
}

/// <summary>Immutable fluent builder for repository badges, dependencies, and extension declarations.</summary>
public sealed class CiAiRepositoryBuilder
{
    private readonly CiAiBadgeDefinition[] _badges;
    private readonly CiAiDependencyReference[] _dependencies;
    private readonly CiAiExtensionDefinition[] _extensions;

    public CiAiRepositoryBuilder()
        : this(Array.Empty<CiAiBadgeDefinition>(), Array.Empty<CiAiDependencyReference>(), Array.Empty<CiAiExtensionDefinition>()) { }

    private CiAiRepositoryBuilder(
        CiAiBadgeDefinition[] badges,
        CiAiDependencyReference[] dependencies,
        CiAiExtensionDefinition[] extensions)
    {
        _badges = badges;
        _dependencies = dependencies;
        _extensions = extensions;
    }

    public CiAiRepositoryBuilder WithBadge(CiAiBadgeDefinition badge)
    {
        ArgumentNullException.ThrowIfNull(badge);
        if (_badges.Any(x => string.Equals(x.Id, badge.Id, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Badge '{badge.Id}' is already enabled.");
        return new(_badges.Append(badge).ToArray(), _dependencies, _extensions);
    }

    public CiAiRepositoryBuilder WithDependency(CiAiDependencyReference dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        if (_dependencies.Any(x => string.Equals(x.Repository, dependency.Repository, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Capability, dependency.Capability, StringComparison.Ordinal)))
            throw new InvalidOperationException("The dependency is already declared.");
        return new(_badges, _dependencies.Append(dependency).ToArray(), _extensions);
    }

    public CiAiRepositoryBuilder ExtendWith(CiAiExtensionDefinition extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        if (_extensions.Any(x => string.Equals(x.Id, extension.Id, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Extension '{extension.Id}' is already registered.");
        return new(_badges, _dependencies, _extensions.Append(extension).ToArray());
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string Repository, string? Capability)>
    {
        public static StringTupleComparer Instance { get; } = new();
        public bool Equals((string Repository, string? Capability) x, (string Repository, string? Capability) y)
            => string.Equals(x.Repository, y.Repository, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(x.Capability, y.Capability, StringComparison.Ordinal);
        public int GetHashCode((string Repository, string? Capability) obj)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Repository),
                obj.Capability is null ? 0 : StringComparer.Ordinal.GetHashCode(obj.Capability));
    }

    public CiAiRepositoryConfiguration Build(string repository)
    {
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must use canonical owner/repository identity.", nameof(repository));
        var owner = repository[..repository.IndexOf('/')];
        var dependencies = _dependencies.Select(item => item.ResolveForOwner(owner)).ToArray();
        if (dependencies.Any(item => string.Equals(item.Repository, repository, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A repository cannot declare itself as a dependency.");
        var duplicate = dependencies.GroupBy(item => (item.Repository, item.Capability), StringTupleComparer.Instance)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Dependency '{duplicate.Key.Repository}' is declared more than once.");
        return new(repository, _badges, dependencies, _extensions);
    }
}

/// <summary>
/// Deterministically stamps the managed badge block in README.md files. Text outside the block is preserved.
/// Call StampRepositoryReadmes from a trusted build/maintenance host; it never changes CI policy or Git state.
/// </summary>
public static partial class CiAiReadmeBadgeStamper
{
    public const string StartMarker = "<!-- CIAI:BADGES:START -->";
    public const string EndMarker = "<!-- CIAI:BADGES:END -->";

    public static string Stamp(string readme, CiAiRepositoryConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(readme);
        ArgumentNullException.ThrowIfNull(configuration);
        var newline = readme.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var block = RenderBlock(configuration, newline);
        var start = readme.IndexOf(StartMarker, StringComparison.Ordinal);
        var end = readme.IndexOf(EndMarker, StringComparison.Ordinal);
        if ((start >= 0) != (end >= 0) || (start >= 0 && end < start))
            throw new InvalidDataException("README contains an incomplete or misordered CiAi badge region.");

        if (start >= 0)
        {
            var afterEnd = end + EndMarker.Length;
            return readme[..start] + block + readme[afterEnd..];
        }

        var lines = readme.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var heading = Array.FindIndex(lines, line => line.StartsWith("# ", StringComparison.Ordinal));
        if (heading < 0)
            return block + newline + readme;
        var insertion = string.Join(newline, lines.Take(heading + 1));
        var remainder = string.Join(newline, lines.Skip(heading + 1));
        return insertion + newline + newline + block + (remainder.Length == 0 ? string.Empty : newline + remainder);
    }

    public static int StampRepositoryReadmes(string repositoryRoot, CiAiRepositoryConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(configuration);
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var changed = 0;
        foreach (var path in EnumerateReadmes(root))
        {
            var original = File.ReadAllText(path, Encoding.UTF8);
            var stamped = Stamp(original, configuration);
            if (string.Equals(original, stamped, StringComparison.Ordinal))
                continue;
            File.WriteAllText(path, stamped, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            changed++;
        }
        return changed;
    }

    private static string RenderBlock(CiAiRepositoryConfiguration configuration, string newline)
    {
        var builder = new StringBuilder();
        builder.Append(StartMarker).Append(newline);
        foreach (var badge in configuration.Badges.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            builder.Append("[![")
                .Append(EscapeMarkdown(badge.Label))
                .Append("](").Append(badge.ImageUrl(configuration.Repository)).Append(")](")
                .Append(badge.TargetUrl(configuration.Repository)).Append(')')
                .Append(newline);
        }
        builder.Append(EndMarker);
        return builder.ToString();
    }

    private static string EscapeMarkdown(string value)
        => value.Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);
}
