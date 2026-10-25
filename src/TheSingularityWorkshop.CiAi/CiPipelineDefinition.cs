namespace TheSingularityWorkshop.CiAi;

/// <summary>Events that can select a declarative pipeline definition.</summary>
public enum CiPipelineTrigger
{
    Manual = 0,
    PullRequest = 1,
    PushToMain = 2,
    Tag = 3,
    Schedule = 4
}

/// <summary>Capabilities that require explicit policy treatment.</summary>
public enum CiStepCapability
{
    None = 0,
    PublishPackage = 1,
    Deploy = 2,
    CancelWorkflow = 3
}

/// <summary>An immutable, inspectable step description. This alpha model never executes its operation.</summary>
public sealed record CiStepDefinition
{
    internal CiStepDefinition(
        string id,
        string name,
        Type resultType,
        CiStepCapability capability,
        bool requiresApproval,
        Delegate operation)
    {
        Id = id;
        Name = name;
        ResultType = resultType;
        Capability = capability;
        RequiresApproval = requiresApproval;
        Operation = operation;
    }

    public string Id { get; }
    public string Name { get; }
    public Type ResultType { get; }
    public CiStepCapability Capability { get; }
    public bool RequiresApproval { get; }

    // Retained for a future executor; deliberately not public or invoked by the definition model.
    internal Delegate Operation { get; }
}

/// <summary>An immutable pipeline definition; it describes work but does not execute it.</summary>
public sealed record CiPipelineDefinition
{
    internal CiPipelineDefinition(string repository, IReadOnlyList<CiPipelineTrigger> triggers,
        IReadOnlyList<CiStepDefinition> steps)
    {
        Repository = repository;
        Triggers = Array.AsReadOnly(triggers.ToArray());
        Steps = Array.AsReadOnly(steps.ToArray());
    }

    public string Repository { get; }
    public IReadOnlyList<CiPipelineTrigger> Triggers { get; }
    public IReadOnlyList<CiStepDefinition> Steps { get; }

    /// <summary>Returns deterministic, human-readable recipe text without invoking step delegates.</summary>
    public string ToRecipe()
    {
        var lines = new List<string>
        {
            $"Pipeline: {Repository}",
            $"Triggers: {string.Join(", ", Triggers.Select(FormatTrigger))}"
        };

        for (var index = 0; index < Steps.Count; index++)
        {
            var step = Steps[index];
            var policy = step.RequiresApproval ? $" [approval required: {step.Capability}]" :
                step.Capability != CiStepCapability.None ? $" [capability: {step.Capability}]" : string.Empty;
            lines.Add($"{index + 1}. {step.Name} (id: {step.Id}, result: {step.ResultType.Name}){policy}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatTrigger(CiPipelineTrigger trigger) => trigger switch
    {
        CiPipelineTrigger.Manual => "manual",
        CiPipelineTrigger.PullRequest => "pull request",
        CiPipelineTrigger.PushToMain => "push to main",
        CiPipelineTrigger.Tag => "tag",
        CiPipelineTrigger.Schedule => "schedule",
        _ => throw new InvalidOperationException("Pipeline contains an unknown trigger.")
    };
}

/// <summary>Builds a declarative CI recipe. No registered operation is invoked during construction or inspection.</summary>
public sealed class CiPipelineBuilder
{
    private readonly string _repository;
    private readonly List<CiPipelineTrigger> _triggers = [];
    private readonly List<CiStepDefinition> _steps = [];
    private bool _built;

    public CiPipelineBuilder(string repository)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must use canonical owner/repository identifiers.", nameof(repository));
        _repository = repository;
    }

    public CiPipelineBuilder On(CiPipelineTrigger trigger)
    {
        EnsureMutable();
        if (!Enum.IsDefined(trigger))
            throw new ArgumentOutOfRangeException(nameof(trigger));
        if (!_triggers.Contains(trigger))
            _triggers.Add(trigger);
        return this;
    }

    /// <summary>Registers a named operation for a future executor; the delegate is not called here.</summary>
    public CiPipelineBuilder Add<T>(
        string name,
        Func<T> operation,
        string? id = null,
        CiStepCapability capability = CiStepCapability.None,
        bool requiresApproval = false)
    {
        EnsureMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(operation);
        if (!Enum.IsDefined(capability))
            throw new ArgumentOutOfRangeException(nameof(capability));
        if (requiresApproval && capability == CiStepCapability.None)
            throw new ArgumentException("An approval requirement must name a privileged capability.", nameof(capability));
        if (IsPrivileged(capability) && !requiresApproval)
            throw new ArgumentException("Privileged capabilities require an explicit approval gate.", nameof(requiresApproval));

        var stepId = string.IsNullOrWhiteSpace(id) ? Slug(name) : id.Trim();
        if (_steps.Any(step => string.Equals(step.Id, stepId, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Step ID '{stepId}' is already registered.", nameof(id));

        _steps.Add(new CiStepDefinition(stepId, name.Trim(), typeof(T), capability, requiresApproval, operation));
        return this;
    }

    public CiPipelineDefinition Build()
    {
        EnsureMutable();
        if (_triggers.Count == 0)
            throw new InvalidOperationException("At least one pipeline trigger must be declared.");
        _built = true;
        return new CiPipelineDefinition(_repository, _triggers, _steps);
    }

    private void EnsureMutable()
    {
        if (_built)
            throw new InvalidOperationException("This builder has already produced a definition.");
    }

    private static bool IsPrivileged(CiStepCapability capability) =>
        capability is CiStepCapability.PublishPackage or CiStepCapability.Deploy or CiStepCapability.CancelWorkflow;

    private static string Slug(string name)
    {
        var characters = name.Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();
        var slug = new string(characters);
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return slug.Trim('-');
    }
}
