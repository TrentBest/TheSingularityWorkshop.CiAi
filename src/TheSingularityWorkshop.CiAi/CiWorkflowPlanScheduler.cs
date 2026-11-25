namespace TheSingularityWorkshop.CiAi;

/// <summary>Scheduling decision for one declarative workflow-plan step.</summary>
public enum CiWorkflowStepEligibility
{
    Pending = 0,
    Eligible = 1,
    Completed = 2,
    Skipped = 3,
    Indeterminate = 4
}

/// <summary>Deterministic eligibility decision; an observed outcome is retained for completed steps.</summary>
public sealed record CiWorkflowStepSchedulingDecision(
    string StepId,
    CiWorkflowStepEligibility Eligibility,
    string ReasonCode,
    CiStepOutcome? ObservedOutcome = null);

/// <summary>Pure scheduling result for a validated workflow plan and its observed step outcomes.</summary>
public sealed record CiWorkflowPlanSchedulingReport(
    string Repository,
    string PlanId,
    IReadOnlyList<CiWorkflowStepSchedulingDecision> Steps)
{
    public IReadOnlyList<string> EligibleStepIds =>
        Array.AsReadOnly(Steps.Where(step => step.Eligibility == CiWorkflowStepEligibility.Eligible)
            .Select(step => step.StepId).ToArray());

    public bool HasPendingSteps => Steps.Any(step => step.Eligibility == CiWorkflowStepEligibility.Pending);

    public bool HasIndeterminateSteps => Steps.Any(step => step.Eligibility == CiWorkflowStepEligibility.Indeterminate);
}

/// <summary>
/// Evaluates dependency and condition semantics without executing work. A trusted host remains responsible
/// for invoking eligible operations and supplying authenticated outcomes.
/// </summary>
public static class CiWorkflowPlanScheduler
{
    public static CiWorkflowPlanSchedulingReport Evaluate(
        CiWorkflowPlanDraft draft,
        IReadOnlyDictionary<string, CiStepOutcome> observedOutcomes)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(observedOutcomes);

        var validation = CiWorkflowPlanValidator.Validate(draft);
        if (!validation.IsValid)
            throw new ArgumentException(
                "Workflow plan is invalid: " + string.Join(", ", validation.Errors),
                nameof(draft));

        var byId = draft.Steps.ToDictionary(step => step.Id, StringComparer.Ordinal);
        foreach (var pair in observedOutcomes)
        {
            if (!byId.ContainsKey(pair.Key))
                throw new ArgumentException($"Outcome references unknown workflow step '{pair.Key}'.", nameof(observedOutcomes));
            if (!Enum.IsDefined(pair.Value))
                throw new ArgumentOutOfRangeException(nameof(observedOutcomes),
                    $"Outcome for workflow step '{pair.Key}' is invalid.");
        }

        var decisions = new List<CiWorkflowStepSchedulingDecision>(draft.Steps.Count);
        foreach (var stepId in validation.TopologicalStepIds)
        {
            var step = byId[stepId];
            if (observedOutcomes.TryGetValue(stepId, out var ownOutcome))
            {
                decisions.Add(new(stepId, CiWorkflowStepEligibility.Completed, "step.already-terminal", ownOutcome));
                continue;
            }

            var dependencies = step.DependsOn.Select(id => observedOutcomes.TryGetValue(id, out var outcome)
                ? (Found: true, Outcome: outcome)
                : (Found: false, Outcome: default(CiStepOutcome))).ToArray();

            if (dependencies.Any(dependency => !dependency.Found))
            {
                decisions.Add(new(stepId, CiWorkflowStepEligibility.Pending, "dependency.pending"));
                continue;
            }

            var dependencyOutcomes = dependencies.Select(dependency => dependency.Outcome).ToArray();
            var hasUncertainOutcome = dependencyOutcomes.Any(outcome =>
                outcome is CiStepOutcome.Indeterminate or CiStepOutcome.Blocked or CiStepOutcome.Cancelled);
            var anyFailed = dependencyOutcomes.Contains(CiStepOutcome.Failed);

            var decision = step.Condition switch
            {
                CiWorkflowStepCondition.AllDependenciesSucceeded when hasUncertainOutcome =>
                    new CiWorkflowStepSchedulingDecision(stepId, CiWorkflowStepEligibility.Indeterminate,
                        "dependency.outcome-uncertain"),
                CiWorkflowStepCondition.AllDependenciesSucceeded when dependencyOutcomes.All(
                    outcome => outcome == CiStepOutcome.Succeeded) =>
                    new CiWorkflowStepSchedulingDecision(stepId, CiWorkflowStepEligibility.Eligible,
                        "condition.all-dependencies-succeeded"),
                CiWorkflowStepCondition.AllDependenciesSucceeded =>
                    new CiWorkflowStepSchedulingDecision(stepId, CiWorkflowStepEligibility.Skipped,
                        "dependency.not-all-succeeded"),
                CiWorkflowStepCondition.Always =>
                    new CiWorkflowStepSchedulingDecision(stepId, CiWorkflowStepEligibility.Eligible,
                        "condition.always"),
                CiWorkflowStepCondition.AnyDependencyFailed when anyFailed =>
                    new CiWorkflowStepSchedulingDecision(stepId, CiWorkflowStepEligibility.Eligible,
                        "condition.any-dependency-failed"),
                CiWorkflowStepCondition.AnyDependencyFailed when hasUncertainOutcome =>
                    new CiWorkflowStepSchedulingDecision(stepId, CiWorkflowStepEligibility.Indeterminate,
                        "dependency.outcome-uncertain"),
                CiWorkflowStepCondition.AnyDependencyFailed =>
                    new CiWorkflowStepSchedulingDecision(stepId, CiWorkflowStepEligibility.Skipped,
                        "dependency.no-failure"),
                CiWorkflowStepCondition.AllDependenciesCompleted =>
                    new CiWorkflowStepSchedulingDecision(stepId, CiWorkflowStepEligibility.Eligible,
                        "condition.all-dependencies-completed"),
                _ => throw new InvalidOperationException("Validated workflow plan contains an unknown condition.")
            };
            decisions.Add(decision);
        }

        return new CiWorkflowPlanSchedulingReport(
            draft.Repository,
            draft.PlanId,
            Array.AsReadOnly(decisions.ToArray()));
    }
}
