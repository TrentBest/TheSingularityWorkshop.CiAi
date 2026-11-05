using TheSingularityWorkshop.GrammarAi;
using TheSingularityWorkshop.ProtocolAi;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Defines the initial context-free token grammar for a CiAi workflow recipe.</summary>
/// <remarks>
/// GrammarAi owns generic production representation and protocol-qualified terminals. CiAi owns
/// the semantic categories and this recipe grammar. This is a structural grammar over known tokens,
/// not a text parser, complete workflow-plan validator, provider adapter, or authorization policy.
/// </remarks>
public static class CiWorkflowGrammarCatalog
{
    public const ulong GrammarId = 0x434941494752414D; // "CIAIGRAM"

    private const ulong Plan = 1;
    private const ulong Workflow = 2;
    private const ulong Trigger = 3;
    private const ulong Steps = 4;
    private const ulong Step = 5;
    private const ulong StepKind = 6;
    private const ulong Condition = 7;
    private const ulong Domain = 8;
    private const ulong Dependencies = 9;
    private const ulong EvidenceList = 10;
    private const ulong EvidenceKind = 11;
    private const ulong Action = 12;

    /// <summary>Builds the CiAi grammar against the exact active category definitions supplied by the host.</summary>
    public static GrammarDefinition Create(CiWorkflowProtocolSet protocols)
    {
        ArgumentNullException.ThrowIfNull(protocols);
        var builder = new GrammarBuilder(GrammarId, "CiAi Workflow Recipe", Plan);
        builder
            .Rule(1, Plan, N(Workflow))
            .Rule(2, Workflow, T(protocols.WorkflowFields, CiWorkflowProtocolCatalog.FieldWorkflow),
                T(protocols.WorkflowFields, CiWorkflowProtocolCatalog.FieldRepository),
                T(protocols.WorkflowFields, CiWorkflowProtocolCatalog.FieldTrigger), N(Trigger),
                T(protocols.WorkflowFields, CiWorkflowProtocolCatalog.FieldStep), N(Steps))
            .Rule(3, Trigger, T(protocols.Triggers, CiWorkflowProtocolCatalog.TriggerPush))
            .Rule(4, Trigger, T(protocols.Triggers, CiWorkflowProtocolCatalog.TriggerPullRequest))
            .Rule(5, Trigger, T(protocols.Triggers, CiWorkflowProtocolCatalog.TriggerSchedule))
            .Rule(6, Trigger, T(protocols.Triggers, CiWorkflowProtocolCatalog.TriggerWorkflowDispatch))
            .Rule(7, Steps, N(Step), N(Steps))
            .Rule(8, Steps)
            .Rule(9, Step, T(protocols.WorkflowFields, CiWorkflowProtocolCatalog.FieldStep), N(StepKind),
                T(protocols.WorkflowFields, CiWorkflowProtocolCatalog.FieldCondition), N(Condition),
                T(protocols.WorkflowFields, CiWorkflowProtocolCatalog.FieldDomain), N(Domain),
                T(protocols.WorkflowFields, CiWorkflowProtocolCatalog.FieldDependency), N(Dependencies),
                T(protocols.WorkflowFields, CiWorkflowProtocolCatalog.FieldEvidence), N(EvidenceList),
                T(protocols.WorkflowFields, CiWorkflowProtocolCatalog.FieldAction), N(Action))
            .Rule(10, StepKind, T(protocols.WorkflowSteps, CiWorkflowProtocolCatalog.StepRestore))
            .Rule(11, StepKind, T(protocols.WorkflowSteps, CiWorkflowProtocolCatalog.StepBuild))
            .Rule(12, StepKind, T(protocols.WorkflowSteps, CiWorkflowProtocolCatalog.StepTest))
            .Rule(13, StepKind, T(protocols.WorkflowSteps, CiWorkflowProtocolCatalog.StepPackage))
            .Rule(14, StepKind, T(protocols.WorkflowSteps, CiWorkflowProtocolCatalog.StepBenchmark))
            .Rule(15, StepKind, T(protocols.WorkflowSteps, CiWorkflowProtocolCatalog.StepObserveEvidence))
            .Rule(16, StepKind, T(protocols.WorkflowSteps, CiWorkflowProtocolCatalog.StepPublish))
            .Rule(17, StepKind, T(protocols.WorkflowSteps, CiWorkflowProtocolCatalog.StepDeploy))
            .Rule(18, Condition, T(protocols.Conditions, CiWorkflowProtocolCatalog.ConditionAllDependenciesSucceeded))
            .Rule(19, Condition, T(protocols.Conditions, CiWorkflowProtocolCatalog.ConditionAlways))
            .Rule(20, Condition, T(protocols.Conditions, CiWorkflowProtocolCatalog.ConditionAnyDependencyFailed))
            .Rule(21, Condition, T(protocols.Conditions, CiWorkflowProtocolCatalog.ConditionAllDependenciesCompleted))
            .Rule(22, Domain, T(protocols.Domains, CiWorkflowProtocolCatalog.DomainBuildTest))
            .Rule(23, Domain, T(protocols.Domains, CiWorkflowProtocolCatalog.DomainCodeQuality))
            .Rule(24, Domain, T(protocols.Domains, CiWorkflowProtocolCatalog.DomainSecurity))
            .Rule(25, Domain, T(protocols.Domains, CiWorkflowProtocolCatalog.DomainCompatibility))
            .Rule(26, Domain, T(protocols.Domains, CiWorkflowProtocolCatalog.DomainDocumentation))
            .Rule(27, Domain, T(protocols.Domains, CiWorkflowProtocolCatalog.DomainPackage))
            .Rule(28, Domain, T(protocols.Domains, CiWorkflowProtocolCatalog.DomainReleaseCandidate))
            .Rule(29, Domain, T(protocols.Domains, CiWorkflowProtocolCatalog.DomainPublication))
            .Rule(30, Domain, T(protocols.Domains, CiWorkflowProtocolCatalog.DomainDeployment))
            .Rule(31, Domain, T(protocols.Domains, CiWorkflowProtocolCatalog.DomainDeploymentValidation))
            .Rule(32, Domain, T(protocols.Domains, CiWorkflowProtocolCatalog.DomainPerformance))
            .Rule(33, Domain, T(protocols.Domains, CiWorkflowProtocolCatalog.DomainIntegration))
            .Rule(34, Domain, T(protocols.Domains, CiWorkflowProtocolCatalog.DomainArtifactHandoff))
            .Rule(35, Domain, T(protocols.Domains, CiWorkflowProtocolCatalog.DomainProvenance))
            .Rule(36, Domain, T(protocols.Domains, CiWorkflowProtocolCatalog.DomainScheduledMaintenance))
            .Rule(37, Domain, T(protocols.Domains, CiWorkflowProtocolCatalog.DomainConcurrency))
            .Rule(38, Domain, T(protocols.Domains, CiWorkflowProtocolCatalog.DomainRecovery))
            .Rule(39, Domain, T(protocols.Domains, CiWorkflowProtocolCatalog.DomainMultiRepository))
            .Rule(40, Domain, T(protocols.Domains, CiWorkflowProtocolCatalog.DomainMatrix))
            .Rule(41, Domain, T(protocols.Domains, CiWorkflowProtocolCatalog.DomainObservability))
            .Rule(42, Domain, T(protocols.Domains, CiWorkflowProtocolCatalog.DomainRepositoryHygiene))
            .Rule(43, Domain, T(protocols.Domains, CiWorkflowProtocolCatalog.DomainSmoke))
            .Rule(44, Dependencies, T(protocols.WorkflowFields, CiWorkflowProtocolCatalog.FieldStep), N(Dependencies))
            .Rule(45, Dependencies)
            .Rule(46, EvidenceList, N(EvidenceKind), N(EvidenceList))
            .Rule(47, EvidenceList)
            .Rule(48, EvidenceKind, T(protocols.EvidenceKinds, CiWorkflowProtocolCatalog.EvidenceBuild))
            .Rule(49, EvidenceKind, T(protocols.EvidenceKinds, CiWorkflowProtocolCatalog.EvidenceTest))
            .Rule(50, EvidenceKind, T(protocols.EvidenceKinds, CiWorkflowProtocolCatalog.EvidencePackage))
            .Rule(51, EvidenceKind, T(protocols.EvidenceKinds, CiWorkflowProtocolCatalog.EvidenceBenchmark))
            .Rule(52, EvidenceKind, T(protocols.EvidenceKinds, CiWorkflowProtocolCatalog.EvidenceSourceProvenance))
            .Rule(53, EvidenceKind, T(protocols.EvidenceKinds, CiWorkflowProtocolCatalog.EvidenceContract))
            .Rule(54, EvidenceKind, T(protocols.EvidenceKinds, CiWorkflowProtocolCatalog.EvidenceArtifactDigest))
            .Rule(55, EvidenceKind, T(protocols.EvidenceKinds, CiWorkflowProtocolCatalog.EvidenceAuthorization))
            .Rule(56, Action, T(protocols.Actions, CiWorkflowProtocolCatalog.ActionBuild))
            .Rule(57, Action, T(protocols.Actions, CiWorkflowProtocolCatalog.ActionTest))
            .Rule(58, Action, T(protocols.Actions, CiWorkflowProtocolCatalog.ActionPublish))
            .Rule(59, Action, T(protocols.Actions, CiWorkflowProtocolCatalog.ActionDeploy));

        return builder.Build();
    }

    /// <summary>Finds protocol terminals absent from the supplied active category registry.</summary>
    public static IReadOnlyList<GrammarProtocolReference> FindUnresolvedProtocolReferences(
        GrammarDefinition grammar,
        CiWorkflowProtocolSet protocols)
    {
        ArgumentNullException.ThrowIfNull(grammar);
        ArgumentNullException.ThrowIfNull(protocols);
        return grammar.FindUnresolvedProtocolReferences(reference =>
            CiWorkflowProtocolCatalog.Resolves(protocols, reference.ProtocolId, reference.SymbolId));
    }

    private static GrammarSymbol N(ulong id) => GrammarSymbol.NonTerminal(id);

    private static GrammarSymbol T(ProtocolDefinition category, ulong symbolId)
    {
        var symbol = category.Symbols.FirstOrDefault(item => item.Id == symbolId);
        if (symbol.Id == 0)
            throw new ArgumentException($"CiAi category '{category.Name}' does not define member '{symbolId}'.", nameof(category));
        var reference = category.Reference(symbol.Name);
        return GrammarSymbol.Terminal(new GrammarProtocolReference(reference.ProtocolId, reference.SymbolId));
    }
}
