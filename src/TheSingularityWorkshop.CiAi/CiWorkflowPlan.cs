using System.Text.RegularExpressions;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Controls when a declarative step is eligible after its dependencies reach terminal outcomes.</summary>
public enum CiWorkflowStepCondition
{
    AllDependenciesSucceeded = 0,
    Always = 1,
    AnyDependencyFailed = 2,
    AllDependenciesCompleted = 3
}

/// <summary>Untrusted declarative input. Validation must succeed before a host considers execution.</summary>
public sealed record CiWorkflowPlanStepDraft(
    string Id,
    string Name,
    string DomainId,
    IReadOnlyList<string> DependsOn,
    CiWorkflowStepCondition Condition = CiWorkflowStepCondition.AllDependenciesSucceeded);

/// <summary>Pure workflow plan input: data only, with no delegates, credentials, or side effects.</summary>
public sealed record CiWorkflowPlanDraft(
    string Repository,
    string PlanId,
    IReadOnlyList<CiWorkflowPlanStepDraft> Steps);

/// <summary>Validation result for a workflow plan. An invalid plan has no executable order.</summary>
public sealed record CiWorkflowPlanValidation(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> TopologicalStepIds);

/// <summary>Validates references and dependency graphs and renders deterministic recipe text.</summary>
public static class CiWorkflowPlanValidator
{
    private static readonly Regex StableId = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private static readonly Regex FullSha = new("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant);

    public static CiWorkflowPlanValidation Validate(CiWorkflowPlanDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(draft.Repository) || !RepositoryIdentity.IsValid(draft.Repository))
            errors.Add("plan.repository-invalid");
        if (string.IsNullOrWhiteSpace(draft.PlanId) || !StableId.IsMatch(draft.PlanId))
            errors.Add("plan.id-invalid");
        if (draft.Steps is null)
        {
            errors.Add("plan.steps-required");
            return Result(errors, Array.Empty<string>());
        }

        var byId = new Dictionary<string, CiWorkflowPlanStepDraft>(StringComparer.Ordinal);
        foreach (var step in draft.Steps)
        {
            if (step is null)
            {
                errors.Add("step.null");
                continue;
            }
            if (string.IsNullOrWhiteSpace(step.Id) || !StableId.IsMatch(step.Id))
                errors.Add($"step.id-invalid:{step.Id ?? "<null>"}");
            else if (!byId.TryAdd(step.Id, step))
                errors.Add($"step.id-duplicate:{step.Id}");

            if (string.IsNullOrWhiteSpace(step.Name))
                errors.Add($"step.name-required:{step.Id ?? "<null>"}");
            if (!Enum.IsDefined(step.Condition))
                errors.Add($"step.condition-invalid:{step.Id ?? "<null>"}");
            if (string.IsNullOrWhiteSpace(step.DomainId) ||
                !CiWorkflowDomainCatalog.TryGet(step.DomainId, out _))
                errors.Add($"step.domain-unknown:{step.Id ?? "<null>"}");
            if (step.DependsOn is null)
            {
                errors.Add($"step.dependencies-required:{step.Id ?? "<null>"}");
                continue;
            }
            if (step.DependsOn.Any(string.IsNullOrWhiteSpace))
                errors.Add($"step.dependency-empty:{step.Id ?? "<null>"}");
            if (step.DependsOn.Distinct(StringComparer.Ordinal).Count() != step.DependsOn.Count)
                errors.Add($"step.dependency-duplicate:{step.Id}");
            if (step.DependsOn.Contains(step.Id, StringComparer.Ordinal))
                errors.Add($"step.self-dependency:{step.Id}");
        }

        foreach (var step in byId.Values)
        {
            if (step.DependsOn is null) continue;
            foreach (var dependency in step.DependsOn)
            {
                if (!byId.ContainsKey(dependency))
                    errors.Add($"step.dependency-unknown:{step.Id}:{dependency}");
            }
        }

        var order = TopologicalOrder(byId, out var cycle);
        if (cycle)
            errors.Add("plan.dependency-cycle");
        if (errors.Count > 0)
            return Result(errors.Distinct(StringComparer.Ordinal).OrderBy(e => e, StringComparer.Ordinal).ToArray(),
                Array.Empty<string>());
        return Result(Array.Empty<string>(), order);
    }

    public static string RenderRecipe(CiWorkflowPlanDraft draft)
    {
        var validation = Validate(draft);
        if (!validation.IsValid)
            throw new ArgumentException("Workflow plan is invalid: " + string.Join(", ", validation.Errors), nameof(draft));

        var byId = draft.Steps.ToDictionary(step => step.Id, StringComparer.Ordinal);
        var lines = new List<string>
        {
            $"Workflow plan: {draft.PlanId}",
            $"Repository: {draft.Repository}",
            "Execution order:"
        };
        for (var i = 0; i < validation.TopologicalStepIds.Count; i++)
        {
            var step = byId[validation.TopologicalStepIds[i]];
            var dependencies = step.DependsOn.Count == 0 ? "none" : string.Join(", ", step.DependsOn.OrderBy(x => x, StringComparer.Ordinal));
            lines.Add($"{i + 1}. {step.Id} — {step.Name.Trim()} [domain: {step.DomainId}; condition: {FormatCondition(step.Condition)}; depends on: {dependencies}]");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static IReadOnlyList<string> TopologicalOrder(
        IReadOnlyDictionary<string, CiWorkflowPlanStepDraft> byId, out bool hasCycle)
    {
        var remaining = byId.Keys.ToDictionary(id => id,
            id => byId[id].DependsOn?.Where(byId.ContainsKey).Distinct(StringComparer.Ordinal).Count() ?? 0,
            StringComparer.Ordinal);
        var dependents = byId.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var step in byId.Values)
            if (step.DependsOn is not null)
                foreach (var dependency in step.DependsOn.Where(byId.ContainsKey).Distinct(StringComparer.Ordinal))
                    dependents[dependency].Add(step.Id);

        var ready = new SortedSet<string>(remaining.Where(pair => pair.Value == 0).Select(pair => pair.Key), StringComparer.Ordinal);
        var order = new List<string>(byId.Count);
        while (ready.Count > 0)
        {
            var current = ready.Min!;
            ready.Remove(current);
            order.Add(current);
            foreach (var dependent in dependents[current].OrderBy(id => id, StringComparer.Ordinal))
            {
                remaining[dependent]--;
                if (remaining[dependent] == 0) ready.Add(dependent);
            }
        }
        hasCycle = order.Count != byId.Count;
        return order.AsReadOnly();
    }

    private static string FormatCondition(CiWorkflowStepCondition condition) => condition switch
    {
        CiWorkflowStepCondition.AllDependenciesSucceeded => "all dependencies succeeded",
        CiWorkflowStepCondition.Always => "always after dependencies complete",
        CiWorkflowStepCondition.AnyDependencyFailed => "any dependency failed",
        CiWorkflowStepCondition.AllDependenciesCompleted => "all dependencies completed",
        _ => throw new InvalidOperationException("Workflow plan contains an unknown condition.")
    };

    private static CiWorkflowPlanValidation Result(IEnumerable<string> errors, IReadOnlyList<string> order)
    {
        var materialized = errors.ToArray();
        return new CiWorkflowPlanValidation(materialized.Length == 0,
            Array.AsReadOnly(materialized), Array.AsReadOnly(order.ToArray()));
    }
}
