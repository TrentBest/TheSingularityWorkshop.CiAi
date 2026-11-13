using System.Collections.ObjectModel;
using TheSingularityWorkshop.GrammarAi;

namespace TheSingularityWorkshop.CiAi;

public sealed record CiWorkflowGrammarProposal(ulong DefinitionId, uint SchemaVersion, uint WireVersion,
    string VocabularySnapshotDigest, string Json, ulong WorkflowInstanceId, IReadOnlyDictionary<string, ulong> StepInstanceIds);

/// <summary>Versioned CI workflow structure. GrammarAi validates shape; CiAi retains policy and evidence semantics.</summary>
public static class CiWorkflowGrammarMap
{
    public const string GrammarAiRevision = "2582362ad233faeffe82c2de28d464efffb0cda4";
    public const string ProtocolAiRevision = "024ca3f2fac486d5aefa6f276c5203d5bdfe3bcf";
    public const ulong DefinitionId = 0x43494149UL;
    public const uint SchemaVersion = 2;
    public const ulong WorkflowNodeId = 1, StepNodeId = 2, InputNodeId = 3, OutputNodeId = 4;
    public const ulong WorkflowStepsSlotId = 101, StepDependenciesSlotId = 102, WorkflowTriggersSlotId = 103;
    public const ulong StepActionSlotId = 201, StepInputsSlotId = 202, StepOutputsSlotId = 203;
    public const ulong StepCapabilitiesSlotId = 204, StepSideEffectsSlotId = 205, StepEvidenceRolesSlotId = 206, StepOutcomeRolesSlotId = 207, StepConditionSlotId = 208;
    public const ulong InputNameSlotId = 301, InputTypeSlotId = 302, OutputNameSlotId = 401, OutputTypeSlotId = 402;
    public const int MaximumSteps = 256, MaximumTypedValuesPerStep = 32, MaximumDeclarationsPerStep = 64;

    private static readonly GrammarStructureDefinition DefinitionValue = new(
        DefinitionId, "CiAi.WorkflowPlan",
        new[] { new GrammarNodeDefinition(WorkflowNodeId,"Workflow"), new GrammarNodeDefinition(StepNodeId,"Step"),
            new GrammarNodeDefinition(InputNodeId,"InputDeclaration"), new GrammarNodeDefinition(OutputNodeId,"OutputDeclaration") },
        new[]
        {
            new GrammarSlotDefinition(WorkflowStepsSlotId,WorkflowNodeId,"Steps",StepNodeId,1,MaximumSteps),
            new GrammarSlotDefinition(StepDependenciesSlotId,StepNodeId,"DependsOn",StepNodeId,0,MaximumSteps),
            new GrammarSlotDefinition(WorkflowTriggersSlotId,WorkflowNodeId,"Triggers",GrammarSlotTarget.ExternalProtocolCategory(CiWorkflowVocabularyCatalog.TriggerCategoryId),1,32),
            new GrammarSlotDefinition(StepActionSlotId,StepNodeId,"Action",GrammarSlotTarget.ExternalProtocolCategory(CiWorkflowVocabularyCatalog.ActionCategoryId),1,1),
            new GrammarSlotDefinition(StepInputsSlotId,StepNodeId,"Inputs",InputNodeId,0,MaximumTypedValuesPerStep),
            new GrammarSlotDefinition(StepOutputsSlotId,StepNodeId,"Outputs",OutputNodeId,0,MaximumTypedValuesPerStep),
            new GrammarSlotDefinition(StepCapabilitiesSlotId,StepNodeId,"Capabilities",GrammarSlotTarget.ExternalProtocolCategory(CiWorkflowVocabularyCatalog.CapabilityCategoryId),0,MaximumDeclarationsPerStep),
            new GrammarSlotDefinition(StepSideEffectsSlotId,StepNodeId,"SideEffects",GrammarSlotTarget.ExternalProtocolCategory(CiWorkflowVocabularyCatalog.SideEffectCategoryId),0,MaximumDeclarationsPerStep),
            new GrammarSlotDefinition(StepEvidenceRolesSlotId,StepNodeId,"ExpectedEvidence",GrammarSlotTarget.ExternalProtocolCategory(CiWorkflowVocabularyCatalog.EvidenceRoleCategoryId),0,MaximumDeclarationsPerStep),
            new GrammarSlotDefinition(StepOutcomeRolesSlotId,StepNodeId,"ExpectedOutcomes",GrammarSlotTarget.ExternalProtocolCategory(CiWorkflowVocabularyCatalog.OutcomeRoleCategoryId),0,MaximumDeclarationsPerStep),
            new GrammarSlotDefinition(StepConditionSlotId,StepNodeId,"Condition",GrammarSlotTarget.ExternalProtocolCategory(CiWorkflowVocabularyCatalog.StepConditionCategoryId),1,1),
            new GrammarSlotDefinition(InputNameSlotId,InputNodeId,"Name",GrammarSlotTarget.ExternalProtocolCategory(CiWorkflowVocabularyCatalog.InputNameCategoryId),1,1),
            new GrammarSlotDefinition(InputTypeSlotId,InputNodeId,"Type",GrammarSlotTarget.ExternalProtocolCategory(CiWorkflowVocabularyCatalog.DataTypeCategoryId),1,1),
            new GrammarSlotDefinition(OutputNameSlotId,OutputNodeId,"Name",GrammarSlotTarget.ExternalProtocolCategory(CiWorkflowVocabularyCatalog.OutputNameCategoryId),1,1),
            new GrammarSlotDefinition(OutputTypeSlotId,OutputNodeId,"Type",GrammarSlotTarget.ExternalProtocolCategory(CiWorkflowVocabularyCatalog.DataTypeCategoryId),1,1)
        }, SchemaVersion);

    public static GrammarStructureDefinition Definition => DefinitionValue;

    public static CiWorkflowGrammarProposal CreateProposal(CiWorkflowPlanDraft plan, CiWorkflowVocabularySnapshot? vocabularySnapshot = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var validation = CiWorkflowPlanValidator.Validate(plan);
        if (!validation.IsValid) throw new ArgumentException("A workflow plan must pass CiAi validation: " + string.Join(", ", validation.Errors), nameof(plan));
        if (plan.Steps.Count == 0 || plan.Steps.Count > MaximumSteps) throw new ArgumentException("Workflow step count is outside the supported range.", nameof(plan));
        if (plan.Triggers is null || plan.Triggers.Count == 0 || plan.Triggers.Count > 32) throw new ArgumentException("A workflow requires 1..32 triggers.", nameof(plan));
        var snapshot = vocabularySnapshot ?? CiWorkflowVocabularyCatalog.CreateSnapshot();
        const ulong workflowId = 1;
        ulong nextId = 2;
        var stepIds = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var nodes = new List<GrammarNodeInstance> { new(workflowId, WorkflowNodeId) };
        var relationships = new List<GrammarRelationship>();

        foreach (var trigger in plan.Triggers)
            relationships.Add(External(workflowId,WorkflowTriggersSlotId,CiWorkflowVocabularyCatalog.TriggerCategoryId,trigger,snapshot));
        foreach (var step in plan.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.ActionId)) throw new ArgumentException($"Step '{step.Id}' requires an action identity.", nameof(plan));
            if ((step.Inputs?.Count ?? 0) > MaximumTypedValuesPerStep || (step.Outputs?.Count ?? 0) > MaximumTypedValuesPerStep)
                throw new ArgumentException($"Step '{step.Id}' exceeds typed declaration limits.", nameof(plan));
            var id = nextId++; stepIds.Add(step.Id,id); nodes.Add(new GrammarNodeInstance(id,StepNodeId));
            relationships.Add(new GrammarRelationship(workflowId,WorkflowStepsSlotId,id));
        }
        foreach (var step in plan.Steps)
        {
            var id = stepIds[step.Id];
            relationships.Add(External(id,StepActionSlotId,CiWorkflowVocabularyCatalog.ActionCategoryId,step.ActionId!,snapshot));
            relationships.Add(External(id,StepConditionSlotId,CiWorkflowVocabularyCatalog.StepConditionCategoryId,ConditionValue(step.Condition),snapshot));
            foreach (var dependency in step.DependsOn) relationships.Add(new GrammarRelationship(id,StepDependenciesSlotId,stepIds[dependency]));
            AddRefs(step.Capabilities,StepCapabilitiesSlotId,CiWorkflowVocabularyCatalog.CapabilityCategoryId);
            AddRefs(step.SideEffects,StepSideEffectsSlotId,CiWorkflowVocabularyCatalog.SideEffectCategoryId);
            AddRefs(step.EvidenceRoles,StepEvidenceRolesSlotId,CiWorkflowVocabularyCatalog.EvidenceRoleCategoryId);
            AddRefs(step.OutcomeRoles,StepOutcomeRolesSlotId,CiWorkflowVocabularyCatalog.OutcomeRoleCategoryId);
            AddTyped(step.Inputs,InputNodeId,StepInputsSlotId,InputNameSlotId,CiWorkflowVocabularyCatalog.InputNameCategoryId);
            AddTyped(step.Outputs,OutputNodeId,StepOutputsSlotId,OutputNameSlotId,CiWorkflowVocabularyCatalog.OutputNameCategoryId);

            void AddRefs(IReadOnlyList<string>? values,ulong slot,ulong category)
            {
                if(values is null)return;
                if(values.Count>MaximumDeclarationsPerStep)throw new ArgumentException("Step metadata exceeds the declaration limit.",nameof(plan));
                foreach(var value in values)relationships.Add(External(id,slot,category,value,snapshot));
            }
            void AddTyped(IReadOnlyList<CiWorkflowTypedValueDraft>? values,ulong kind,ulong parentSlot,ulong nameSlot,ulong nameCategory)
            {
                if(values is null)return;
                foreach(var value in values)
                {
                    if(value is null || string.IsNullOrWhiteSpace(value.NameId) || string.IsNullOrWhiteSpace(value.TypeId))
                        throw new ArgumentException("A typed input/output declaration is invalid.",nameof(plan));
                    var declarationId=nextId++; nodes.Add(new GrammarNodeInstance(declarationId,kind));
                    relationships.Add(new GrammarRelationship(id,parentSlot,declarationId));
                    relationships.Add(External(declarationId,nameSlot,nameCategory,value.NameId,snapshot));
                    relationships.Add(External(declarationId,kind==InputNodeId?InputTypeSlotId:OutputTypeSlotId,
                        CiWorkflowVocabularyCatalog.DataTypeCategoryId,value.TypeId,snapshot));
                }
            }
        }
        if(nodes.Count>2048 || relationships.Count>4096)throw new ArgumentException("Workflow graph exceeds the bounded CI structure size.",nameof(plan));
        var instance=new GrammarStructureInstance(DefinitionValue,nodes,relationships,snapshot);
        var json=GrammarStructureProposalCodec.Serialize(instance);
        return new CiWorkflowGrammarProposal(DefinitionValue.Id,DefinitionValue.Version,GrammarStructureProposalCodec.CurrentWireVersion,
            snapshot.Digest,json,workflowId,new ReadOnlyDictionary<string,ulong>(stepIds));
    }

    public static GrammarStructureInstance ValidateProposal(string json,CiWorkflowVocabularySnapshot? vocabularySnapshot=null) =>
        GrammarStructureProposalCodec.Deserialize(json,DefinitionValue,vocabularySnapshot??CiWorkflowVocabularyCatalog.CreateSnapshot());

    public static GrammarStructureInstance ValidateProposal(CiWorkflowGrammarProposal proposal,CiWorkflowVocabularySnapshot? vocabularySnapshot=null)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var snapshot=vocabularySnapshot??CiWorkflowVocabularyCatalog.CreateSnapshot();
        if(proposal.DefinitionId!=DefinitionValue.Id || proposal.SchemaVersion!=DefinitionValue.Version)
            throw new ArgumentException("Proposal schema identity/version does not match the active definition.",nameof(proposal));
        if(proposal.WireVersion!=GrammarStructureProposalCodec.CurrentWireVersion)
            throw new ArgumentException("Proposal wire version does not match the active codec.",nameof(proposal));
        if(!string.Equals(proposal.VocabularySnapshotDigest,snapshot.Digest,StringComparison.Ordinal))
            throw new ArgumentException("Proposal vocabulary snapshot differs from the active request catalog.",nameof(proposal));
        if(proposal.StepInstanceIds is null)throw new ArgumentException("Step-instance correlation is required.",nameof(proposal));
        var instance=ValidateProposal(proposal.Json,snapshot);
        if(!instance.TryGetNode(proposal.WorkflowInstanceId,out var workflow)||workflow!.NodeDefinitionId!=WorkflowNodeId)
            throw new ArgumentException("Workflow instance identity is invalid.",nameof(proposal));
        var stepNodes=instance.Nodes.Where(n=>n.NodeDefinitionId==StepNodeId).Select(n=>n.Id).ToHashSet();
        if(instance.Nodes.Count(n=>n.NodeDefinitionId==WorkflowNodeId)!=1 ||
            proposal.StepInstanceIds.Count!=stepNodes.Count ||
            proposal.StepInstanceIds.Values.Distinct().Count()!=proposal.StepInstanceIds.Count ||
            !proposal.StepInstanceIds.Values.All(stepNodes.Contains))
            throw new ArgumentException("Step-instance correlation does not match the graph.",nameof(proposal));
        return instance;
    }

    private static string ConditionValue(CiWorkflowStepCondition condition) => condition switch
    {
        CiWorkflowStepCondition.AllDependenciesSucceeded => "all_dependencies_succeeded",
        CiWorkflowStepCondition.Always => "always",
        CiWorkflowStepCondition.AnyDependencyFailed => "any_dependency_failed",
        CiWorkflowStepCondition.AllDependenciesCompleted => "all_dependencies_completed",
        _ => throw new ArgumentOutOfRangeException(nameof(condition))
    };

    private static GrammarRelationship External(ulong parent,ulong slot,ulong category,string value,CiWorkflowVocabularySnapshot snapshot) =>
        new(parent,slot,CiWorkflowVocabularyCatalog.Reference(snapshot,category,value));
}
