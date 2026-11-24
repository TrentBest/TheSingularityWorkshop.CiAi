namespace TheSingularityWorkshop.CiAi;

/// <summary>One declarative entry point for repository composition, dependencies, badges, and ADP budgets.</summary>
public static class CiAi
{
    public static CiAiRepositoryBuilder ForRepository(string repository) => new(repository);

    public static CiAiRepositoryBuilder WithBadge(CiAiBadgeDefinition badge) =>
        new CiAiRepositoryBuilder().WithBadge(badge);

    public static CiAiRepositoryBuilder WithDependency(CiAiDependencyExpression dependency) =>
        new CiAiRepositoryBuilder().WithDependency(dependency);

    public static CiAiRepositoryBuilder ExtendWith(CiAiExtensionDefinition extension) =>
        new CiAiRepositoryBuilder().ExtendWith(extension);

    public static CiAiOrderedDependencyFallback Or(params string[] repositories) => new(repositories);

    public static class Logic
    {
        public static CiAiOrderedDependencyFallback Or(params string[] repositories) => new(repositories);
    }
}

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
        if (workflowFile.Contains('/') || workflowFile.Contains('\\\\') ||
            !(workflowFile.EndsWith(".yml", StringComparison.Ordinal) || workflowFile.EndsWith(".yaml", StringComparison.Ordinal)))
            throw new ArgumentException("Badge workflow must be a workflow filename.", nameof(workflowFile));
        Id = id;
        Label = label;
        WorkflowFile = workflowFile;
    }

    public string Id { get; }
    public string Label { get; }
    public string WorkflowFile { get; }
    public string ImageUrl(string repository) => $"https://github.com/{ValidateRepository(repository)}/actions/workflows/{WorkflowFile}/badge.svg";
    public string TargetUrl(string repository) => $"https://github.com/{ValidateRepository(repository)}/actions/workflows/{WorkflowFile}";
    private static string ValidateRepository(string repository) =>
        RepositoryIdentity.IsValid(repository) ? repository :
        throw new ArgumentException("Repository must use canonical owner/repository identity.", nameof(repository));
}

public static class Badges
{
    public static CiAiBadgeDefinition SithPolicy() => new("sith-policy", "Sith Policy", "sith-policy.yml");
}

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

public static class Gauntlet
{
    public static CiAiExtensionDefinition Registrar(string version = "1") => new("Gauntlet.Registrar", version);
}

public enum CiAiLlmPriority { Low = 0, Normal = 1, High = 2, Critical = 3 }

public sealed record CiAiAdpSettings
{
    public CiAiAdpSettings(int maximumRepairAttempts = 1, bool requireHumanApprovalForWrites = true)
    {
        if (maximumRepairAttempts is < 0 or > 10) throw new ArgumentOutOfRangeException(nameof(maximumRepairAttempts));
        MaximumRepairAttempts = maximumRepairAttempts;
        RequireHumanApprovalForWrites = requireHumanApprovalForWrites;
    }
    public int MaximumRepairAttempts { get; }
    public bool RequireHumanApprovalForWrites { get; }
}

public sealed record CiAiLlmAllocation
{
    public CiAiLlmAllocation(string provider, string model, int maximumConcurrentCalls = 1,
        int maximumCallsPerWindow = 60, long maximumTokensPerWindow = 100_000,
        TimeSpan? window = null, CiAiLlmPriority priority = CiAiLlmPriority.Normal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (maximumConcurrentCalls < 1) throw new ArgumentOutOfRangeException(nameof(maximumConcurrentCalls));
        if (maximumCallsPerWindow < 1) throw new ArgumentOutOfRangeException(nameof(maximumCallsPerWindow));
        if (maximumTokensPerWindow < 1) throw new ArgumentOutOfRangeException(nameof(maximumTokensPerWindow));
        if ((window ?? TimeSpan.FromHours(1)) <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));
        if (!Enum.IsDefined(priority)) throw new ArgumentOutOfRangeException(nameof(priority));
        Provider = provider.Trim(); Model = model.Trim();
        MaximumConcurrentCalls = maximumConcurrentCalls; MaximumCallsPerWindow = maximumCallsPerWindow;
        MaximumTokensPerWindow = maximumTokensPerWindow; Window = window ?? TimeSpan.FromHours(1); Priority = priority;
    }
    public string Provider { get; }
    public string Model { get; }
    public int MaximumConcurrentCalls { get; }
    public int MaximumCallsPerWindow { get; }
    public long MaximumTokensPerWindow { get; }
    public TimeSpan Window { get; }
    public CiAiLlmPriority Priority { get; }
}

public abstract record CiAiDependencyExpression
{
    internal abstract string Key { get; }
    internal abstract void AddRepositories(ICollection<string> target);
}

public sealed record CiAiRepositoryDependency : CiAiDependencyExpression
{
    public CiAiRepositoryDependency(string repository)
    {
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Dependency must use canonical owner/repository identity.", nameof(repository));
        Repository = repository;
    }
    public string Repository { get; }
    internal override string Key => "repo:" + Repository;
    internal override void AddRepositories(ICollection<string> target) => target.Add(Repository);
}

public sealed record CiAiOrderedDependencyFallback : CiAiDependencyExpression
{
    public CiAiOrderedDependencyFallback(IEnumerable<string> repositories)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        var values = repositories.ToArray();
        if (values.Length < 2 || values.Any(x => !RepositoryIdentity.IsValid(x)) ||
            values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Length)
            throw new ArgumentException("Fallbacks require at least two unique canonical repository identities.", nameof(repositories));
        Repositories = Array.AsReadOnly(values);
    }
    public IReadOnlyList<string> Repositories { get; }
    internal override string Key => "or:" + string.Join("|", Repositories);
    internal override void AddRepositories(ICollection<string> target)
    {
        foreach (var repository in Repositories) target.Add(repository);
    }
}

/// <summary>Immutable result of one repository's CiAi declaration. It grants no execution or write authority.</summary>
public sealed record CiAiRepositoryDefinition
{
    internal CiAiRepositoryDefinition(string repository, IReadOnlyList<CiAiBadgeDefinition> badges,
        IReadOnlyList<CiAiDependencyExpression> dependencies, IReadOnlyList<CiAiExtensionDefinition> extensions,
        IReadOnlyList<string> pipelines, CiAiAdpSettings? adp, CiAiLlmAllocation? allocation)
    {
        Repository = repository;
        Badges = Array.AsReadOnly(badges.ToArray());
        Dependencies = Array.AsReadOnly(dependencies.ToArray());
        Extensions = Array.AsReadOnly(extensions.ToArray());
        NamedDevelopmentPipelines = Array.AsReadOnly(pipelines.ToArray());
        AdpSettings = adp; LlmAllocation = allocation;
    }
    public const string DefaultDevelopmentPipeline = "default";
    public string Repository { get; }
    public IReadOnlyList<CiAiBadgeDefinition> Badges { get; }
    public IReadOnlyList<CiAiDependencyExpression> Dependencies { get; }
    public IReadOnlyList<CiAiExtensionDefinition> Extensions { get; }
    public IReadOnlyList<string> NamedDevelopmentPipelines { get; }
    public IReadOnlyList<string> DevelopmentPipelines => NamedDevelopmentPipelines.Count == 0
        ? Array.AsReadOnly(new[] { DefaultDevelopmentPipeline }) : NamedDevelopmentPipelines;
    public CiAiAdpSettings? AdpSettings { get; }
    public CiAiLlmAllocation? LlmAllocation { get; }
    public bool AutonomousDevelopmentEnabled => AdpSettings is not null;
    public IReadOnlyList<string> DependencyRepositories
    {
        get
        {
            var result = new List<string>();
            foreach (var item in Dependencies) item.AddRepositories(result);
            return Array.AsReadOnly(result.ToArray());
        }
    }
}

/// <summary>
/// One immutable fluent builder. Each call returns a new builder; Build seals a snapshot without
/// enabling ADP, scheduling work, invoking an LLM, executing CI, or granting repository-write authority.
/// </summary>
public sealed class CiAiRepositoryBuilder
{
    private readonly string? _repository;
    private readonly CiAiBadgeDefinition[] _badges;
    private readonly CiAiDependencyExpression[] _dependencies;
    private readonly CiAiExtensionDefinition[] _extensions;
    private readonly string[] _pipelines;
    private readonly CiAiAdpSettings? _adp;
    private readonly CiAiLlmAllocation? _allocation;

    public CiAiRepositoryBuilder() : this(null, [], [], [], [], null, null) { }
    internal CiAiRepositoryBuilder(string repository) : this(
        RepositoryIdentity.IsValid(repository) ? repository :
            throw new ArgumentException("Repository must use canonical owner/repository identity.", nameof(repository)),
        [], [], [], [], null, null) { }

    private CiAiRepositoryBuilder(string? repository, CiAiBadgeDefinition[] badges,
        CiAiDependencyExpression[] dependencies, CiAiExtensionDefinition[] extensions,
        string[] pipelines, CiAiAdpSettings? adp, CiAiLlmAllocation? allocation)
    {
        _repository = repository; _badges = badges; _dependencies = dependencies;
        _extensions = extensions; _pipelines = pipelines; _adp = adp; _allocation = allocation;
    }

    public CiAiRepositoryBuilder WithBadge(CiAiBadgeDefinition badge)
    {
        ArgumentNullException.ThrowIfNull(badge);
        if (_badges.Any(x => x.Id == badge.Id)) throw new InvalidOperationException($"Badge '{badge.Id}' is already enabled.");
        return Copy(badges: _badges.Append(badge).ToArray());
    }
    public CiAiRepositoryBuilder WithDependency(string repository) => WithDependency(new CiAiRepositoryDependency(repository));
    public CiAiRepositoryBuilder WithDependency(CiAiDependencyExpression dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        if (_dependencies.Any(x => x.Key == dependency.Key)) throw new InvalidOperationException("Dependency is already declared.");
        if (_repository is not null && dependency is CiAiRepositoryDependency direct &&
            string.Equals(direct.Repository, _repository, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A repository cannot depend on itself.", nameof(dependency));
        if (_repository is not null && dependency is CiAiOrderedDependencyFallback fallback &&
            fallback.Repositories.Contains(_repository, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("A repository cannot depend on itself.", nameof(dependency));
        return Copy(dependencies: _dependencies.Append(dependency).ToArray());
    }
    public CiAiRepositoryBuilder ExtendWith(CiAiExtensionDefinition extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        if (_extensions.Any(x => x.Id == extension.Id)) throw new InvalidOperationException($"Extension '{extension.Id}' is already registered.");
        return Copy(extensions: _extensions.Append(extension).ToArray());
    }
    public CiAiRepositoryBuilder WithGauntlet() => ExtendWith(Gauntlet.Registrar());
    public CiAiRepositoryBuilder WithNamedDevelopmentPipeline(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var value = name.Trim();
        if (value.Length > 100 || value.Any(char.IsControl)) throw new ArgumentException("Pipeline name is invalid.", nameof(name));
        return _pipelines.Contains(value, StringComparer.Ordinal) ? this : Copy(pipelines: _pipelines.Append(value).ToArray());
    }
    public CiAiRepositoryBuilder WithADP(CiAiAdpSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Copy(adp: settings);
    }
    public CiAiRepositoryBuilder WithLLMAllocation(CiAiLlmAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        return Copy(allocation: allocation);
    }
    public CiAiRepositoryDefinition Build() => Build(_repository ??
        throw new InvalidOperationException("Bind a repository with ForRepository(...) or Build(repository)."));
    public CiAiRepositoryDefinition Build(string repository)
    {
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must use canonical owner/repository identity.", nameof(repository));
        var owner = repository[..repository.IndexOf('/')];
        var resolved = _dependencies.Select(item => item switch
        {
            CiAiRepositoryDependency direct when !RepositoryIdentity.IsValid(direct.Repository) =>
                (CiAiDependencyExpression)new CiAiRepositoryDependency($"{owner}/{direct.Repository}"),
            _ => item
        }).ToArray();
        var names = resolved.SelectMany(item => item switch
        {
            CiAiRepositoryDependency direct => new[] { direct.Repository },
            CiAiOrderedDependencyFallback fallback => fallback.Repositories.ToArray(),
            _ => Array.Empty<string>()
        }).ToArray();
        if (names.Any(x => string.Equals(x, repository, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A repository cannot depend on itself.");
        return new(repository, _badges, resolved, _extensions, _pipelines, _adp, _allocation);
    }
    private CiAiRepositoryBuilder Copy(CiAiBadgeDefinition[]? badges = null,
        CiAiDependencyExpression[]? dependencies = null, CiAiExtensionDefinition[]? extensions = null,
        string[]? pipelines = null, CiAiAdpSettings? adp = null, CiAiLlmAllocation? allocation = null) =>
        new(_repository, badges ?? _badges, dependencies ?? _dependencies, extensions ?? _extensions,
            pipelines ?? _pipelines, adp ?? _adp, allocation ?? _allocation);
}
