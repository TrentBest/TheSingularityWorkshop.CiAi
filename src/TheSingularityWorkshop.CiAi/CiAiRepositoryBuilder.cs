namespace TheSingularityWorkshop.CiAi;

/// <summary>Scheduling priority requested by a repository for its bounded LLM allocation.</summary>
public enum CiAiLlmPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

/// <summary>Optional autonomous-development authority settings. CI composition does not require this object.</summary>
public sealed record CiAiAdpSettings
{
    public CiAiAdpSettings(int maximumRepairAttempts = 1, bool requireHumanApprovalForWrites = true)
    {
        if (maximumRepairAttempts is < 0 or > 10)
            throw new ArgumentOutOfRangeException(nameof(maximumRepairAttempts));
        MaximumRepairAttempts = maximumRepairAttempts;
        RequireHumanApprovalForWrites = requireHumanApprovalForWrites;
    }

    public int MaximumRepairAttempts { get; }
    public bool RequireHumanApprovalForWrites { get; }
}

/// <summary>
/// A repository's requested share of a provider/model capacity pool.
/// Provider-wide hard limits remain owned by the central allocator, not by this declaration.
/// </summary>
public sealed record CiAiLlmAllocation
{
    internal CiAiLlmAllocation(
        string provider,
        string model,
        int maximumConcurrentCalls,
        int maximumCallsPerWindow,
        long maximumTokensPerWindow,
        TimeSpan window,
        CiAiLlmPriority priority)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (maximumConcurrentCalls < 1) throw new ArgumentOutOfRangeException(nameof(maximumConcurrentCalls));
        if (maximumCallsPerWindow < 1) throw new ArgumentOutOfRangeException(nameof(maximumCallsPerWindow));
        if (maximumTokensPerWindow < 1) throw new ArgumentOutOfRangeException(nameof(maximumTokensPerWindow));
        if (window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));
        if (!Enum.IsDefined(priority)) throw new ArgumentOutOfRangeException(nameof(priority));

        Provider = provider.Trim();
        Model = model.Trim();
        MaximumConcurrentCalls = maximumConcurrentCalls;
        MaximumCallsPerWindow = maximumCallsPerWindow;
        MaximumTokensPerWindow = maximumTokensPerWindow;
        Window = window;
        Priority = priority;
    }

    public string Provider { get; }
    public string Model { get; }
    public int MaximumConcurrentCalls { get; }
    public int MaximumCallsPerWindow { get; }
    public long MaximumTokensPerWindow { get; }
    public TimeSpan Window { get; }
    public CiAiLlmPriority Priority { get; }
}

/// <summary>Mutable only while declaring a repository's requested LLM capacity.</summary>
public sealed class CiAiLlmAllocationBuilder
{
    private string? _provider;
    private string? _model;
    private int _maximumConcurrentCalls = 1;
    private int _maximumCallsPerWindow = 60;
    private long _maximumTokensPerWindow = 100_000;
    private TimeSpan _window = TimeSpan.FromHours(1);
    private CiAiLlmPriority _priority = CiAiLlmPriority.Normal;

    public CiAiLlmAllocationBuilder SetProvider(string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        _provider = provider.Trim();
        return this;
    }

    public CiAiLlmAllocationBuilder SetModel(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        _model = model.Trim();
        return this;
    }

    public CiAiLlmAllocationBuilder SetMaximumConcurrentCalls(int value)
    {
        _maximumConcurrentCalls = value;
        return this;
    }

    public CiAiLlmAllocationBuilder SetMaximumCallsPerWindow(int value)
    {
        _maximumCallsPerWindow = value;
        return this;
    }

    public CiAiLlmAllocationBuilder SetMaximumTokensPerWindow(long value)
    {
        _maximumTokensPerWindow = value;
        return this;
    }

    public CiAiLlmAllocationBuilder SetWindow(TimeSpan value)
    {
        _window = value;
        return this;
    }

    public CiAiLlmAllocationBuilder SetPriority(CiAiLlmPriority value)
    {
        _priority = value;
        return this;
    }

    internal CiAiLlmAllocation Build()
    {
        if (string.IsNullOrWhiteSpace(_provider))
            throw new InvalidOperationException("An LLM provider must be selected.");
        if (string.IsNullOrWhiteSpace(_model))
            throw new InvalidOperationException("An LLM model must be selected.");
        return new(_provider, _model, _maximumConcurrentCalls, _maximumCallsPerWindow,
            _maximumTokensPerWindow, _window, _priority);
    }
}

/// <summary>One declared upstream repository dependency or ordered fallback expression.</summary>
public abstract record CiAiDependencyExpression
{
    internal abstract string CanonicalKey { get; }
    internal abstract void CollectRepositories(ICollection<string> repositories);
}

/// <summary>A required dependency on one exact repository identity.</summary>
public sealed record CiAiRepositoryDependency : CiAiDependencyExpression
{
    public CiAiRepositoryDependency(string repository)
    {
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Dependency must use canonical owner/repository identity.", nameof(repository));
        Repository = repository;
    }

    public string Repository { get; }
    internal override string CanonicalKey => "repo:" + Repository;

    internal override void CollectRepositories(ICollection<string> repositories) =>
        repositories.Add(Repository);
}

/// <summary>Ordered alternatives. The first eligible provider wins; later entries are fallbacks.</summary>
public sealed record CiAiOrderedDependencyFallback : CiAiDependencyExpression
{
    public CiAiOrderedDependencyFallback(IEnumerable<string> repositories)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        var values = repositories.ToArray();
        if (values.Length < 2)
            throw new ArgumentException("An ordered fallback requires at least two repositories.", nameof(repositories));
        if (values.Any(repository => !RepositoryIdentity.IsValid(repository)))
            throw new ArgumentException("Every fallback must use canonical owner/repository identity.", nameof(repositories));
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ArgumentException("Fallback repositories must be unique and ordered.", nameof(repositories));
        Repositories = Array.AsReadOnly(values);
    }

    public IReadOnlyList<string> Repositories { get; }
    internal override string CanonicalKey => "or:" + string.Join("|", Repositories);

    internal override void CollectRepositories(ICollection<string> repositories)
    {
        foreach (var repository in Repositories) repositories.Add(repository);
    }
}

/// <summary>Immutable declarative CiAi configuration for one repository.</summary>
public sealed record CiAiRepositoryDefinition
{
    internal CiAiRepositoryDefinition(
        string repository,
         IReadOnlyList<string> namedDevelopmentPipelines,
        IReadOnlyList<CiAiDependencyExpression> dependencies,
        CiAiAdpSettings? adpSettings,
        CiAiLlmAllocation? llmAllocation)
    {
        Repository = repository;
        NamedDevelopmentPipelines = Array.AsReadOnly(namedDevelopmentPipelines.ToArray());
        Dependencies = Array.AsReadOnly(dependencies.ToArray());
        AdpSettings = adpSettings;
        LlmAllocation = llmAllocation;
    }

    public const string DefaultDevelopmentPipeline = "default";
    public string Repository { get; }
    public IReadOnlyList<string> NamedDevelopmentPipelines { get; }
    public IReadOnlyList<string> DevelopmentPipelines => NamedDevelopmentPipelines.Count == 0
        ? Array.AsReadOnly(new[] { DefaultDevelopmentPipeline })
        : NamedDevelopmentPipelines;
    public IReadOnlyList<CiAiDependencyExpression> Dependencies { get; }
    public CiAiAdpSettings? AdpSettings { get; }
    public CiAiLlmAllocation? LlmAllocation { get; }
    public bool AutonomousDevelopmentEnabled => AdpSettings is not null;

    /// <summary>All declared candidate upstreams, preserving declaration and fallback order.</summary>
    public IReadOnlyList<string> DependencyRepositories
    {
        get
        {
            var repositories = new List<string>();
            foreach (var dependency in Dependencies) dependency.CollectRepositories(repositories);
            return Array.AsReadOnly(repositories.ToArray());
        }
    }
}

/// <summary>
/// Fluent, declarative CI composition. It describes relationships and budgets; it does not schedule,
/// invoke providers, execute CI, or grant repository-write authority.
/// </summary>
public static class CiAi
{
    public static CiAiRepositoryBuilder ForRepository(string repository) => new(repository);

    public static class Logic
    {
        /// <summary>Creates ordered fallback semantics: use the first available repository, then the next.</summary>
        public static CiAiOrderedDependencyFallback Or(params string[] repositories) =>
            new(repositories);
    }
}

/// <summary>Builds one immutable repository-level CiAi declaration.</summary>
public sealed class CiAiRepositoryBuilder
{
    private readonly string _repository;
    private readonly List<CiAiDependencyExpression> _dependencies = [];
    private readonly List<string> _namedDevelopmentPipelines = [];
    private CiAiAdpSettings? _adpSettings;
    private CiAiLlmAllocation? _llmAllocation;
    private bool _built;

    internal CiAiRepositoryBuilder(string repository)
    {
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must use canonical owner/repository identity.", nameof(repository));
        _repository = repository;
    }

    public CiAiRepositoryBuilder WithDependency(string repository) =>
        WithDependency(new CiAiRepositoryDependency(repository));

    public CiAiRepositoryBuilder WithDependency(CiAiDependencyExpression dependency)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(dependency);
        if (dependency is CiAiRepositoryDependency direct &&
            string.Equals(direct.Repository, _repository, StringComparison.Ordinal))
            throw new ArgumentException("A repository cannot depend on itself.", nameof(dependency));
        if (dependency is CiAiOrderedDependencyFallback fallback &&
            fallback.Repositories.Contains(_repository, StringComparer.Ordinal))
            throw new ArgumentException("A repository cannot include itself in its fallback dependencies.", nameof(dependency));
        if (_dependencies.Any(existing => existing.CanonicalKey == dependency.CanonicalKey))
            throw new ArgumentException("The dependency expression is already declared.", nameof(dependency));
        _dependencies.Add(dependency);
        return this;
    }

    public CiAiRepositoryBuilder WithNamedDevelopmentPipeline(string name)
    {
        EnsureMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Trim();
        if (normalized.Length > 100 || normalized.Any(char.IsControl))
            throw new ArgumentException("Pipeline names must be at most 100 printable characters.", nameof(name));
        if (!_namedDevelopmentPipelines.Contains(normalized, StringComparer.Ordinal))
            _namedDevelopmentPipelines.Add(normalized);
        return this;
    }

    public CiAiRepositoryBuilder WithADP(CiAiAdpSettings settings)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(settings);
        _adpSettings = settings;
        return this;
    }

    public CiAiRepositoryBuilder WithLLMAllocation(Action<CiAiLlmAllocationBuilder> configure)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new CiAiLlmAllocationBuilder();
        configure(builder);
        _llmAllocation = builder.Build();
        return this;
    }

    public CiAiRepositoryDefinition Build()
    {
        EnsureMutable();
        _built = true;
        return new(_repository, _namedDevelopmentPipelines, _dependencies, _adpSettings, _llmAllocation);
    }

    private void EnsureMutable()
    {
        if (_built) throw new InvalidOperationException("This builder has already produced a definition.");
    }
}
