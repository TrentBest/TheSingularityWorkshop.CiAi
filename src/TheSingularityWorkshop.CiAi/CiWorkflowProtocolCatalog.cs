using TheSingularityWorkshop.ProtocolAi;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Immutable set of CiAi-owned ProtocolAi vocabularies. Each ProtocolDefinition is one semantic category;
/// its symbol IDs are local to that category and references remain qualified by the definition ID.
/// </summary>
public sealed class CiWorkflowProtocolSet
{
    internal CiWorkflowProtocolSet(
        ProtocolDefinition workflowFields,
        ProtocolDefinition repositories,
        ProtocolDefinition triggers,
        ProtocolDefinition workflowSteps,
        ProtocolDefinition conditions,
        ProtocolDefinition domains,
        ProtocolDefinition evidenceKinds,
        ProtocolDefinition actions,
        ProtocolDefinition outcomeStates)
    {
        WorkflowFields = workflowFields;
        Repositories = repositories;
        Triggers = triggers;
        WorkflowSteps = workflowSteps;
        Conditions = conditions;
        Domains = domains;
        EvidenceKinds = evidenceKinds;
        Actions = actions;
        OutcomeStates = outcomeStates;
        Definitions = Array.AsReadOnly([
            workflowFields, repositories, triggers, workflowSteps, conditions,
            domains, evidenceKinds, actions, outcomeStates
        ]);
    }

    public ProtocolDefinition WorkflowFields { get; }
    public ProtocolDefinition Repositories { get; }
    public ProtocolDefinition Triggers { get; }
    public ProtocolDefinition WorkflowSteps { get; }
    public ProtocolDefinition Conditions { get; }
    public ProtocolDefinition Domains { get; }
    public ProtocolDefinition EvidenceKinds { get; }
    public ProtocolDefinition Actions { get; }
    public ProtocolDefinition OutcomeStates { get; }
    public IReadOnlyList<ProtocolDefinition> Definitions { get; }

    /// <summary>Resolves a protocol-qualified member only within its owning semantic category.</summary>
    public bool Resolves(ulong protocolId, ulong symbolId) =>
        Definitions.Any(definition => definition.Id == protocolId &&
            definition.Symbols.Any(symbol => symbol.Id == symbolId));
}

/// <summary>
/// Owns the stable ProtocolAi categories used by CiAi workflow recipes.
/// </summary>
/// <remarks>
/// A ProtocolDefinition is the category identity; ProtocolSymbol IDs identify members within it.
/// Unknown values remain literals until the host registers them in the appropriate category.
/// These definitions do not execute, authorize, or prove CI operations.
/// </remarks>
public static class CiWorkflowProtocolCatalog
{
    public const ulong WorkflowFieldsProtocolId = 0x43494149574F5246; // "CIAIWORF"
    public const ulong RepositoriesProtocolId = 0x434941495245504F; // "CIAIREPO"
    public const ulong TriggersProtocolId = 0x4349414954524947; // "CIAITRIG"
    public const ulong WorkflowStepsProtocolId = 0x4349414953544550; // "CIAISTEP"
    public const ulong ConditionsProtocolId = 0x43494149434F4E44; // "CIAICOND"
    public const ulong DomainsProtocolId = 0x43494149444F4D41; // "CIAIDOMA"
    public const ulong EvidenceKindsProtocolId = 0x4349414945564944; // "CIAIEVID"
    public const ulong ActionsProtocolId = 0x434941494143544E; // "CIAIACTN"
    public const ulong OutcomeStatesProtocolId = 0x434941494F555443; // "CIAIOUTC"

    public const ulong FieldWorkflow = 1;
    public const ulong FieldRepository = 2;
    public const ulong FieldTrigger = 3;
    public const ulong FieldStep = 4;
    public const ulong FieldDependency = 5;
    public const ulong FieldCondition = 6;
    public const ulong FieldDomain = 7;
    public const ulong FieldEvidence = 8;
    public const ulong FieldAction = 9;
    public const ulong FieldArtifact = 10;
    public const ulong FieldCandidate = 11;
    public const ulong FieldPolicy = 12;
    public const ulong FieldSourceRevision = 13;
    public const ulong FieldBenchmarkObservation = 14;
    public const ulong FieldApprovalRequirement = 15;

    public const ulong TriggerPush = 1;
    public const ulong TriggerPullRequest = 2;
    public const ulong TriggerSchedule = 3;
    public const ulong TriggerWorkflowDispatch = 4;

    public const ulong StepRestore = 1;
    public const ulong StepBuild = 2;
    public const ulong StepTest = 3;
    public const ulong StepPackage = 4;
    public const ulong StepBenchmark = 5;
    public const ulong StepObserveEvidence = 6;
    public const ulong StepPublish = 7;
    public const ulong StepDeploy = 8;

    public const ulong ConditionAllDependenciesSucceeded = 1;
    public const ulong ConditionAlways = 2;
    public const ulong ConditionAnyDependencyFailed = 3;
    public const ulong ConditionAllDependenciesCompleted = 4;

    public const ulong DomainBuildTest = 1;
    public const ulong DomainCodeQuality = 2;
    public const ulong DomainSecurity = 3;
    public const ulong DomainCompatibility = 4;
    public const ulong DomainDocumentation = 5;
    public const ulong DomainPackage = 6;
    public const ulong DomainReleaseCandidate = 7;
    public const ulong DomainPublication = 8;
    public const ulong DomainDeployment = 9;
    public const ulong DomainDeploymentValidation = 10;
    public const ulong DomainPerformance = 11;
    public const ulong DomainIntegration = 12;
    public const ulong DomainArtifactHandoff = 13;
    public const ulong DomainProvenance = 14;
    public const ulong DomainScheduledMaintenance = 15;
    public const ulong DomainConcurrency = 16;
    public const ulong DomainRecovery = 17;
    public const ulong DomainMultiRepository = 18;
    public const ulong DomainMatrix = 19;
    public const ulong DomainObservability = 20;
    public const ulong DomainRepositoryHygiene = 21;
    public const ulong DomainSmoke = 22;

    public const ulong EvidenceBuild = 1;
    public const ulong EvidenceTest = 2;
    public const ulong EvidencePackage = 3;
    public const ulong EvidenceBenchmark = 4;
    public const ulong EvidenceSourceProvenance = 5;
    public const ulong EvidenceContract = 6;
    public const ulong EvidenceArtifactDigest = 7;
    public const ulong EvidenceAuthorization = 8;

    public const ulong ActionBuild = 1;
    public const ulong ActionTest = 2;
    public const ulong ActionPublish = 3;
    public const ulong ActionDeploy = 4;

    public const ulong OutcomeMissing = 1;
    public const ulong OutcomeUnknown = 2;
    public const ulong OutcomeSkipped = 3;
    public const ulong OutcomeCancelled = 4;
    public const ulong OutcomeBlocked = 5;
    public const ulong OutcomeFailed = 6;
    public const ulong OutcomeSucceeded = 7;

    private static readonly (ulong SymbolId, string DomainId)[] DomainSymbols =
    [
        (DomainBuildTest, "build-test"),
        (DomainCodeQuality, "code-quality"),
        (DomainSecurity, "security"),
        (DomainCompatibility, "compatibility"),
        (DomainDocumentation, "documentation"),
        (DomainPackage, "package"),
        (DomainReleaseCandidate, "release-candidate"),
        (DomainPublication, "publication"),
        (DomainDeployment, "deployment"),
        (DomainDeploymentValidation, "deployment-validation"),
        (DomainPerformance, "performance"),
        (DomainIntegration, "integration"),
        (DomainArtifactHandoff, "artifact-handoff"),
        (DomainProvenance, "provenance"),
        (DomainScheduledMaintenance, "scheduled-maintenance"),
        (DomainConcurrency, "concurrency"),
        (DomainRecovery, "recovery"),
        (DomainMultiRepository, "multi-repository"),
        (DomainMatrix, "matrix"),
        (DomainObservability, "observability"),
        (DomainRepositoryHygiene, "repository-hygiene"),
        (DomainSmoke, "smoke")
    ];

    /// <summary>Creates the immutable category definitions consumed by the initial CI recipe grammar.</summary>
    public static CiWorkflowProtocolSet Create()
    {
        var fields = new ProtocolBuilder(WorkflowFieldsProtocolId, "CiAi Workflow Fields")
            .Define(FieldWorkflow, "Workflow", "workflow")
            .Define(FieldRepository, "Repository", "repository")
            .Define(FieldTrigger, "Trigger", "trigger")
            .Define(FieldStep, "Step", "step")
            .Define(FieldDependency, "Dependency", "depends-on")
            .Define(FieldCondition, "Condition", "condition")
            .Define(FieldDomain, "Domain", "domain")
            .Define(FieldEvidence, "Evidence", "evidence")
            .Define(FieldAction, "Action", "action")
            .Define(FieldArtifact, "Artifact", "artifact")
            .Define(FieldCandidate, "Candidate", "candidate")
            .Define(FieldPolicy, "Policy", "policy")
            .Define(FieldSourceRevision, "SourceRevision", "source-revision")
            .Define(FieldBenchmarkObservation, "BenchmarkObservation", "benchmark-observation")
            .Define(FieldApprovalRequirement, "ApprovalRequirement", "approval-requirement")
            .Build();

        // The host selects and populates repository members for its configured model/session view.
        // An empty stable category is intentional; arbitrary repository strings are not auto-registered.
        var repositories = new ProtocolBuilder(RepositoriesProtocolId, "CiAi Repositories").Build();

        var triggers = new ProtocolBuilder(TriggersProtocolId, "CiAi Triggers")
            .Define(TriggerPush, "Push", "push")
            .Define(TriggerPullRequest, "PullRequest", "pull-request")
            .Define(TriggerSchedule, "Schedule", "schedule")
            .Define(TriggerWorkflowDispatch, "WorkflowDispatch", "workflow-dispatch")
            .Build();

        var steps = new ProtocolBuilder(WorkflowStepsProtocolId, "CiAi Workflow Steps")
            .Define(StepRestore, "Restore", "restore")
            .Define(StepBuild, "Build", "build")
            .Define(StepTest, "Test", "test")
            .Define(StepPackage, "Package", "package")
            .Define(StepBenchmark, "Benchmark", "benchmark")
            .Define(StepObserveEvidence, "ObserveEvidence", "observe-evidence")
            .Define(StepPublish, "Publish", "publish")
            .Define(StepDeploy, "Deploy", "deploy")
            .Build();

        var conditions = new ProtocolBuilder(ConditionsProtocolId, "CiAi Step Conditions")
            .Define(ConditionAllDependenciesSucceeded, "AllDependenciesSucceeded", "all-dependencies-succeeded")
            .Define(ConditionAlways, "Always", "always")
            .Define(ConditionAnyDependencyFailed, "AnyDependencyFailed", "any-dependency-failed")
            .Define(ConditionAllDependenciesCompleted, "AllDependenciesCompleted", "all-dependencies-completed")
            .Build();

        var domains = new ProtocolBuilder(DomainsProtocolId, "CiAi Workflow Domains");
        var knownDomains = CiWorkflowDomainCatalog.All.Select(domain => domain.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var (symbolId, domainId) in DomainSymbols)
        {
            if (!knownDomains.Remove(domainId))
                throw new InvalidOperationException($"CiAi domain '{domainId}' is missing or duplicated.");
            domains.Define(symbolId, "Domain_" + domainId, domainId);
        }
        if (knownDomains.Count > 0)
            throw new InvalidOperationException("CiAi domain identities are incomplete: " +
                string.Join(", ", knownDomains.OrderBy(x => x, StringComparer.Ordinal)));

        var evidenceKinds = new ProtocolBuilder(EvidenceKindsProtocolId, "CiAi Evidence Kinds")
            .Define(EvidenceBuild, "Build", "build")
            .Define(EvidenceTest, "Test", "test")
            .Define(EvidencePackage, "Package", "package")
            .Define(EvidenceBenchmark, "Benchmark", "benchmark-result")
            .Define(EvidenceSourceProvenance, "SourceProvenance", "source-provenance")
            .Define(EvidenceContract, "Contract", "contract-report")
            .Define(EvidenceArtifactDigest, "ArtifactDigest", "artifact-digest")
            .Define(EvidenceAuthorization, "Authorization", "authorization")
            .Build();

        var actions = new ProtocolBuilder(ActionsProtocolId, "CiAi Consequential Actions")
            .Define(ActionBuild, "Build", "build")
            .Define(ActionTest, "Test", "test")
            .Define(ActionPublish, "Publish", "publish")
            .Define(ActionDeploy, "Deploy", "deploy")
            .Build();

        var outcomes = new ProtocolBuilder(OutcomeStatesProtocolId, "CiAi Evidence Outcomes")
            .Define(OutcomeMissing, "Missing", "missing")
            .Define(OutcomeUnknown, "Unknown", "unknown")
            .Define(OutcomeSkipped, "Skipped", "skipped")
            .Define(OutcomeCancelled, "Cancelled", "cancelled")
            .Define(OutcomeBlocked, "Blocked", "blocked")
            .Define(OutcomeFailed, "Failed", "failed")
            .Define(OutcomeSucceeded, "Succeeded", "succeeded")
            .Build();

        return new CiWorkflowProtocolSet(fields, repositories, triggers, steps, conditions,
            domains.Build(), evidenceKinds, actions, outcomes);
    }

    /// <summary>Resolves a protocol-qualified member against the complete CiAi vocabulary registry.</summary>
    public static bool Resolves(CiWorkflowProtocolSet protocols, ulong protocolId, ulong symbolId)
    {
        ArgumentNullException.ThrowIfNull(protocols);
        return protocols.Resolves(protocolId, symbolId);
    }
}
