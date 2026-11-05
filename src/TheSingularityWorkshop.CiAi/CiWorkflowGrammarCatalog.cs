using TheSingularityWorkshop.GrammarAi;
using TheSingularityWorkshop.ProtocolAi;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Defines the initial context-free token grammar for a CiAi workflow recipe.
/// </summary>
/// <remarks>
/// GrammarAi owns generic grammar representation and protocol-terminal references. CiAi owns
/// this grammar, its vocabulary, and the domain-specific plan/evidence rules. The grammar is
/// structural; it is not a text parser, provider adapter, command executor, or authorization policy.
/// </remarks>
public static class CiWorkflowGrammarCatalog
{
    public const ulong GrammarId = 0x434941494752414D; // "CIAIGRAM"

    private const ulong Plan = 1;
    private const ulong Workflow = 2;
    private const ulong Trigger = 3;
    private const ulong Steps = 4;
    private const ulong Step = 5;
    private const ulong Condition = 6;
    private const ulong Domain = 7;
    private const ulong Dependencies = 8;
    private const ulong EvidenceList = 9;
    private const ulong Action = 10;

    /// <summary>Builds the CiAi grammar against the exact vocabulary supplied by the host.</summary>
    public static GrammarDefinition Create(ProtocolDefinition protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        if (protocol.Id != CiWorkflowProtocolCatalog.ProtocolId)
            throw new ArgumentException("The protocol definition is not the CiAi workflow vocabulary.", nameof(protocol));

        var builder = new GrammarBuilder(GrammarId, "CiAi Workflow Recipe", Plan);
        builder
            .Rule(1, Plan, N(Workflow))
            .Rule(2, Workflow, T(protocol, CiWorkflowProtocolCatalog.Workflow),
                T(protocol, CiWorkflowProtocolCatalog.Repository),
                T(protocol, CiWorkflowProtocolCatalog.Trigger), N(Trigger),
                T(protocol, CiWorkflowProtocolCatalog.Step), N(Steps))
            .Rule(3, Trigger, T(protocol, CiWorkflowProtocolCatalog.TriggerPush))
            .Rule(4, Trigger, T(protocol, CiWorkflowProtocolCatalog.TriggerPullRequest))
            .Rule(5, Trigger, T(protocol, CiWorkflowProtocolCatalog.TriggerSchedule))
            .Rule(6, Trigger, T(protocol, CiWorkflowProtocolCatalog.TriggerWorkflowDispatch))
            .Rule(7, Steps, N(Step), N(Steps))
            .Rule(8, Steps)
            .Rule(9, Step, T(protocol, CiWorkflowProtocolCatalog.Step),
                T(protocol, CiWorkflowProtocolCatalog.Condition), N(Condition),
                T(protocol, CiWorkflowProtocolCatalog.Domain), N(Domain),
                T(protocol, CiWorkflowProtocolCatalog.Dependency), N(Dependencies),
                T(protocol, CiWorkflowProtocolCatalog.Evidence), N(EvidenceList),
                T(protocol, CiWorkflowProtocolCatalog.Action), N(Action))
            .Rule(10, Condition, T(protocol, CiWorkflowProtocolCatalog.ConditionAllDependenciesSucceeded))
            .Rule(11, Condition, T(protocol, CiWorkflowProtocolCatalog.ConditionAlways))
            .Rule(12, Condition, T(protocol, CiWorkflowProtocolCatalog.ConditionAnyDependencyFailed))
            .Rule(13, Condition, T(protocol, CiWorkflowProtocolCatalog.ConditionAllDependenciesCompleted))
            .Rule(14, Domain, T(protocol, CiWorkflowProtocolCatalog.DomainBuildTest))
            .Rule(15, Domain, T(protocol, CiWorkflowProtocolCatalog.DomainCodeQuality))
            .Rule(16, Domain, T(protocol, CiWorkflowProtocolCatalog.DomainSecurity))
            .Rule(17, Domain, T(protocol, CiWorkflowProtocolCatalog.DomainCompatibility))
            .Rule(18, Domain, T(protocol, CiWorkflowProtocolCatalog.DomainDocumentation))
            .Rule(19, Domain, T(protocol, CiWorkflowProtocolCatalog.DomainPackage))
            .Rule(20, Domain, T(protocol, CiWorkflowProtocolCatalog.DomainReleaseCandidate))
            .Rule(21, Domain, T(protocol, CiWorkflowProtocolCatalog.DomainPublication))
            .Rule(22, Domain, T(protocol, CiWorkflowProtocolCatalog.DomainDeployment))
            .Rule(23, Domain, T(protocol, CiWorkflowProtocolCatalog.DomainDeploymentValidation))
            .Rule(24, Domain, T(protocol, CiWorkflowProtocolCatalog.DomainPerformance))
            .Rule(25, Domain, T(protocol, CiWorkflowProtocolCatalog.DomainIntegration))
            .Rule(26, Domain, T(protocol, CiWorkflowProtocolCatalog.DomainArtifactHandoff))
            .Rule(27, Domain, T(protocol, CiWorkflowProtocolCatalog.DomainProvenance))
            .Rule(28, Domain, T(protocol, CiWorkflowProtocolCatalog.DomainScheduledMaintenance))
            .Rule(29, Domain, T(protocol, CiWorkflowProtocolCatalog.DomainConcurrency))
            .Rule(30, Domain, T(protocol, CiWorkflowProtocolCatalog.DomainRecovery))
            .Rule(31, Domain, T(protocol, CiWorkflowProtocolCatalog.DomainMultiRepository))
            .Rule(32, Domain, T(protocol, CiWorkflowProtocolCatalog.DomainMatrix))
            .Rule(33, Domain, T(protocol, CiWorkflowProtocolCatalog.DomainObservability))
            .Rule(34, Domain, T(protocol, CiWorkflowProtocolCatalog.DomainRepositoryHygiene))
            .Rule(35, Domain, T(protocol, CiWorkflowProtocolCatalog.DomainSmoke))
            .Rule(36, Dependencies, T(protocol, CiWorkflowProtocolCatalog.Step), N(Dependencies))
            .Rule(37, Dependencies)
            .Rule(38, EvidenceList, T(protocol, CiWorkflowProtocolCatalog.Evidence), N(EvidenceList))
            .Rule(39, EvidenceList)
            .Rule(40, Action, T(protocol, CiWorkflowProtocolCatalog.ActionBuild))
            .Rule(41, Action, T(protocol, CiWorkflowProtocolCatalog.ActionTest))
            .Rule(42, Action, T(protocol, CiWorkflowProtocolCatalog.ActionPublish))
            .Rule(43, Action, T(protocol, CiWorkflowProtocolCatalog.ActionDeploy));

        return builder.Build();
    }

    /// <summary>
    /// Finds protocol terminals not present in the supplied vocabulary.
    /// This is host-side identity resolution, not semantic compatibility or authorization.
    /// </summary>
    public static IReadOnlyList<GrammarProtocolReference> FindUnresolvedProtocolReferences(
        GrammarDefinition grammar,
        ProtocolDefinition protocol)
    {
        ArgumentNullException.ThrowIfNull(grammar);
        ArgumentNullException.ThrowIfNull(protocol);
        return grammar.FindUnresolvedProtocolReferences(reference =>
            CiWorkflowProtocolCatalog.Resolves(protocol, reference.ProtocolId, reference.SymbolId));
    }

    private static GrammarSymbol N(ulong id) => GrammarSymbol.NonTerminal(id);

    private static GrammarSymbol T(ProtocolDefinition protocol, ulong symbolId)
    {
        var symbol = protocol.Symbols.FirstOrDefault(item => item.Id == symbolId);
        if (symbol.Id == 0)
            throw new ArgumentException($"CiAi protocol symbol '{symbolId}' is not defined.", nameof(protocol));
        var reference = protocol.Reference(symbol.Name);
        return GrammarSymbol.Terminal(new GrammarProtocolReference(reference.ProtocolId, reference.SymbolId));
    }
}
