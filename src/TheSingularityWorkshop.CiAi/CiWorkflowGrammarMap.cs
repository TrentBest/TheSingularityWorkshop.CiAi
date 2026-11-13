using System.Collections.ObjectModel;
using TheSingularityWorkshop.GrammarAi;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// A populated, integer-first GrammarAi proposal for one validated CiAi workflow plan.
/// </summary>
/// <remarks>
/// The JSON payload is encoded by GrammarAi. The step-instance map is host-side correlation
/// metadata; it is not embedded as a parallel schema or executable instruction.
/// </remarks>
public sealed record CiWorkflowGrammarProposal(
    ulong DefinitionId,
    uint SchemaVersion,
    uint WireVersion,
    string Json,
    ulong WorkflowInstanceId,
    IReadOnlyDictionary<string, ulong> StepInstanceIds);

/// <summary>
/// Defines and validates CiAi's first versioned workflow knowledge map using GrammarAi's
/// public structural graph and proposal codec.
/// </summary>
/// <remarks>
/// This slice deliberately models workflow membership and ordered step dependencies.
/// CiAi retains action/domain meaning in its existing domain catalog and plan validator.
/// GrammarAi validates only the typed graph. It does not establish policy success,
/// evidence validity, execution authorization, or permission to publish/deploy.
/// </remarks>
public static class CiWorkflowGrammarMap
{
    /// <summary>Exact immutable GrammarAi source revision used by this integration.</summary>
    public const string GrammarAiRevision = "2582362ad233faeffe82c2de28d464efffb0cda4";

    public const ulong DefinitionId = 0x43494149UL;
    public const uint SchemaVersion = 1;
    public const ulong WorkflowNodeId = 1;
    public const ulong StepNodeId = 2;
    public const ulong WorkflowStepsSlotId = 101;
    public const ulong StepDependenciesSlotId = 102;
    public const int MaximumSteps = 256;

    private static readonly GrammarStructureDefinition DefinitionValue = new(
        DefinitionId,
        "CiAi.WorkflowPlan",
        new[]
        {
            new GrammarNodeDefinition(WorkflowNodeId, "Workflow"),
            new GrammarNodeDefinition(StepNodeId, "Step")
        },
        new[]
        {
            new GrammarSlotDefinition(WorkflowStepsSlotId, WorkflowNodeId, "Steps",
                StepNodeId, minimumOccurrences: 1, maximumOccurrences: MaximumSteps),
            new GrammarSlotDefinition(StepDependenciesSlotId, StepNodeId, "DependsOn",
                StepNodeId, minimumOccurrences: 0, maximumOccurrences: MaximumSteps)
        },
        SchemaVersion);

    /// <summary>Gets the immutable versioned structural definition.</summary>
    public static GrammarStructureDefinition Definition => DefinitionValue;

    /// <summary>
    /// Converts a semantically validated CiAi plan into a deterministic GrammarAi graph proposal.
    /// No execution or provider operation is performed.
    /// </summary>
    public static CiWorkflowGrammarProposal CreateProposal(CiWorkflowPlanDraft plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var validation = CiWorkflowPlanValidator.Validate(plan);
        if (!validation.IsValid)
            throw new ArgumentException(
                "A workflow plan must pass CiAi validation before it can be mapped: " +
                string.Join(", ", validation.Errors), nameof(plan));
        if (plan.Steps.Count == 0 || plan.Steps.Count > MaximumSteps)
            throw new ArgumentException(
                "A GrammarAi workflow proposal must contain between 1 and " + MaximumSteps + " steps.",
                nameof(plan));

        const ulong workflowInstanceId = 1;
        var stepIds = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var nodes = new List<GrammarNodeInstance>
        {
            new(workflowInstanceId, WorkflowNodeId)
        };

        for (var index = 0; index < plan.Steps.Count; index++)
        {
            var step = plan.Steps[index];
            var instanceId = checked((ulong)index + 2UL);
            stepIds.Add(step.Id, instanceId);
            nodes.Add(new GrammarNodeInstance(instanceId, StepNodeId));
        }

        var relationships = new List<GrammarRelationship>(plan.Steps.Count * 2);
        foreach (var step in plan.Steps)
            relationships.Add(new GrammarRelationship(
                workflowInstanceId, WorkflowStepsSlotId, stepIds[step.Id]));

        foreach (var step in plan.Steps)
            foreach (var dependencyId in step.DependsOn)
                relationships.Add(new GrammarRelationship(
                    stepIds[step.Id], StepDependenciesSlotId, stepIds[dependencyId]));

        var instance = new GrammarStructureInstance(DefinitionValue, nodes, relationships);
        var json = GrammarStructureProposalCodec.Serialize(instance);

        return new CiWorkflowGrammarProposal(
            DefinitionValue.Id,
            DefinitionValue.Version,
            GrammarStructureProposalCodec.CurrentWireVersion,
            json,
            workflowInstanceId,
            new ReadOnlyDictionary<string, ulong>(stepIds));
    }

    /// <summary>Decodes and structurally validates an AI-facing proposal against this exact schema.</summary>
    public static GrammarStructureInstance ValidateProposal(string json) =>
        GrammarStructureProposalCodec.Deserialize(json, DefinitionValue);

    /// <summary>
    /// Validates both the proposal's declared schema/wire identities and its graph using GrammarAi.
    /// </summary>
    public static GrammarStructureInstance ValidateProposal(CiWorkflowGrammarProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (proposal.DefinitionId != DefinitionValue.Id ||
            proposal.SchemaVersion != DefinitionValue.Version)
            throw new ArgumentException("Proposal schema identity/version does not match the active CiAi workflow definition.", nameof(proposal));
        if (proposal.WireVersion != GrammarStructureProposalCodec.CurrentWireVersion)
            throw new ArgumentException("Proposal wire version does not match the active GrammarAi codec version.", nameof(proposal));
        if (proposal.StepInstanceIds is null)
            throw new ArgumentException("Proposal step-instance correlation is required.", nameof(proposal));

        var instance = ValidateProposal(proposal.Json);
        if (proposal.WorkflowInstanceId == 0 ||
            !instance.TryGetNode(proposal.WorkflowInstanceId, out var workflow) ||
            workflow!.NodeDefinitionId != WorkflowNodeId)
            throw new ArgumentException("Proposal workflow instance identity is invalid.", nameof(proposal));

        var stepNodes = instance.Nodes.Where(node => node.NodeDefinitionId == StepNodeId)
            .Select(node => node.Id).ToHashSet();
        if (instance.Nodes.Count != proposal.StepInstanceIds.Count + 1 ||
            instance.Nodes.Count(node => node.NodeDefinitionId == WorkflowNodeId) != 1 ||
            proposal.StepInstanceIds.Count != stepNodes.Count ||
            proposal.StepInstanceIds.Values.Distinct().Count() != proposal.StepInstanceIds.Count ||
            !proposal.StepInstanceIds.Values.All(stepNodes.Contains))
            throw new ArgumentException("Proposal step-instance correlation does not match the graph.", nameof(proposal));
        foreach (var pair in proposal.StepInstanceIds)
            if (string.IsNullOrWhiteSpace(pair.Key))
                throw new ArgumentException("Proposal contains an invalid step-instance correlation.", nameof(proposal));

        return instance;
    }
}
