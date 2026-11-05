using TheSingularityWorkshop.ProtocolAi;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Owns the stable ProtocolAi vocabulary for CiAi workflow definitions and evidence.
/// </summary>
/// <remarks>
/// These identities describe CI concepts and values. They do not define provider behavior,
/// execute commands, establish evidence, or grant authorization.
/// </remarks>
public static class CiWorkflowProtocolCatalog
{
    public const ulong ProtocolId = 0x4349414950524F54; // "CIAIPROT"

    public const ulong Workflow = 1;
    public const ulong Repository = 2;
    public const ulong Trigger = 3;
    public const ulong Step = 4;
    public const ulong Dependency = 5;
    public const ulong Condition = 6;
    public const ulong Domain = 7;
    public const ulong Evidence = 8;
    public const ulong Action = 9;
    public const ulong Artifact = 10;
    public const ulong Candidate = 11;
    public const ulong Policy = 12;
    public const ulong SourceRevision = 13;
    public const ulong BenchmarkObservation = 14;
    public const ulong ApprovalRequirement = 15;

    public const ulong TriggerPush = 1001;
    public const ulong TriggerPullRequest = 1002;
    public const ulong TriggerSchedule = 1003;
    public const ulong TriggerWorkflowDispatch = 1004;

    public const ulong ConditionAllDependenciesSucceeded = 1101;
    public const ulong ConditionAlways = 1102;
    public const ulong ConditionAnyDependencyFailed = 1103;
    public const ulong ConditionAllDependenciesCompleted = 1104;

    public const ulong ActionBuild = 1301;
    public const ulong ActionTest = 1302;
    public const ulong ActionPublish = 1303;
    public const ulong ActionDeploy = 1304;

    public const ulong OutcomeMissing = 1401;
    public const ulong OutcomeUnknown = 1402;
    public const ulong OutcomeSkipped = 1403;
    public const ulong OutcomeCancelled = 1404;
    public const ulong OutcomeBlocked = 1405;
    public const ulong OutcomeFailed = 1406;
    public const ulong OutcomeSucceeded = 1407;

    /// <summary>Creates the immutable CiAi-owned vocabulary definition.</summary>
    public static ProtocolDefinition Create()
    {
        var builder = new ProtocolBuilder(ProtocolId, "CiAi Workflow Domain");
        builder
            .Define(Workflow, "Workflow", "workflow")
            .Define(Repository, "Repository", "repository")
            .Define(Trigger, "Trigger", "trigger")
            .Define(Step, "Step", "step")
            .Define(Dependency, "Dependency", "depends-on")
            .Define(Condition, "Condition", "condition")
            .Define(Domain, "Domain", "domain")
            .Define(Evidence, "Evidence", "evidence")
            .Define(Action, "Action", "action")
            .Define(Artifact, "Artifact", "artifact")
            .Define(Candidate, "Candidate", "candidate")
            .Define(Policy, "Policy", "policy")
            .Define(SourceRevision, "SourceRevision", "source-revision")
            .Define(BenchmarkObservation, "BenchmarkObservation", "benchmark-observation")
            .Define(ApprovalRequirement, "ApprovalRequirement", "approval-requirement")
            .Define(TriggerPush, "TriggerPush", "push")
            .Define(TriggerPullRequest, "TriggerPullRequest", "pull-request")
            .Define(TriggerSchedule, "TriggerSchedule", "schedule")
            .Define(TriggerWorkflowDispatch, "TriggerWorkflowDispatch", "workflow-dispatch")
            .Define(ConditionAllDependenciesSucceeded, "AllDependenciesSucceeded", "all-dependencies-succeeded")
            .Define(ConditionAlways, "Always", "always")
            .Define(ConditionAnyDependencyFailed, "AnyDependencyFailed", "any-dependency-failed")
            .Define(ConditionAllDependenciesCompleted, "AllDependenciesCompleted", "all-dependencies-completed")
            .Define(ActionBuild, "Build", "build")
            .Define(ActionTest, "Test", "test")
            .Define(ActionPublish, "Publish", "publish")
            .Define(ActionDeploy, "Deploy", "deploy")
            .Define(OutcomeMissing, "Missing", "missing")
            .Define(OutcomeUnknown, "Unknown", "unknown")
            .Define(OutcomeSkipped, "Skipped", "skipped")
            .Define(OutcomeCancelled, "Cancelled", "cancelled")
            .Define(OutcomeBlocked, "Blocked", "blocked")
            .Define(OutcomeFailed, "Failed", "failed")
            .Define(OutcomeSucceeded, "Succeeded", "succeeded");

        AddDomains(builder);
        return builder.Build();
    }

    /// <summary>Returns true only when the supplied protocol-qualified identity exists in this definition.</summary>
    public static bool Resolves(ProtocolDefinition definition, ulong protocolId, ulong symbolId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Id == protocolId && definition.Symbols.Any(symbol => symbol.Id == symbolId);
    }

    private static void AddDomains(ProtocolBuilder builder)
    {
        var domains = CiWorkflowDomainCatalog.All;
        for (var i = 0; i < domains.Count; i++)
            builder.Define(1201UL + (ulong)i, "Domain_" + domains[i].Id, domains[i].Id);
    }
}
