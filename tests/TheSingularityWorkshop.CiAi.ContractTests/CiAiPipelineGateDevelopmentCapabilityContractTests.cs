namespace TheSingularityWorkshop.CiAi.ContractTests;

public static class CiAiPipelineGateDevelopmentCapabilityContractTests
{
    private const string Provider = "TrentBest/TheSingularityWorkshop.Physics";
    private const string CapabilityId = "physics.kinematics.prescribed-motion.v1";
    private const string SourceSha = "1111111111111111111111111111111111111111";
    private const string WorkflowSha = "2222222222222222222222222222222222222222";
    private const string EventSha = "3333333333333333333333333333333333333333";
    private const string CandidateId = Provider + "@" + SourceSha;
    private const string GateId = "physics-prescribed-motion";

    public static int Run()
    {
        var passed = 0;
        void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("CiAiPipelineGateDevelopmentCapabilityContractTests failed: " + name);
            passed++;
        }

        var candidateLeaf = Leaf(requiresProtectedPush: false);
        var candidateExpression = CiAiPipelineGateExpression.ForPipeline("raWWar", candidateLeaf);
        var candidateEvaluation = CiAiRepositoryGateEvaluation.FromDevelopmentCapability(
            Binding(),
            new CiAiDevelopmentCapabilityEvidenceValidation(true, null, Evidence(isProtectedPush: false)));
        var candidateState = CiAiPipelineGateEvaluator.Evaluate(
            candidateExpression,
            new Dictionary<string, CiAiRepositoryGateEvaluation> { [GateId] = candidateEvaluation });
        Check(candidateState == CiAiPipelineGateState.Satisfied, "verified-exact-candidate-capability-satisfies-development-gate");
        Check(CiAiPipelineGateAdmissionPolicy.Evaluate(candidateState).MayAllocateLlm,
            "satisfied-development-capability-admits-consumer-work");

        var protectedLeaf = Leaf(requiresProtectedPush: true);
        var protectedExpression = CiAiPipelineGateExpression.ForPipeline("raWWar-protected", protectedLeaf);
        var candidateOnlyState = CiAiPipelineGateEvaluator.Evaluate(
            protectedExpression,
            new Dictionary<string, CiAiRepositoryGateEvaluation> { [GateId] = candidateEvaluation });
        Check(candidateOnlyState == CiAiPipelineGateState.Waiting, "candidate-evidence-does-not-satisfy-protected-push-gate");
        Check(!CiAiPipelineGateAdmissionPolicy.Evaluate(candidateOnlyState).MayAllocateLlm,
            "protected-push-wait-remains-token-neutral");

        var protectedEvaluation = CiAiRepositoryGateEvaluation.FromDevelopmentCapability(
            Binding(protectedPush: true),
            new CiAiDevelopmentCapabilityEvidenceValidation(true, null, Evidence(isProtectedPush: true)));
        var protectedState = CiAiPipelineGateEvaluator.Evaluate(
            protectedExpression,
            new Dictionary<string, CiAiRepositoryGateEvaluation> { [GateId] = protectedEvaluation });
        Check(protectedState == CiAiPipelineGateState.Satisfied, "verified-protected-push-satisfies-protected-capability-gate");

        var noEvidenceState = CiAiPipelineGateEvaluator.Evaluate(
            candidateExpression,
            new Dictionary<string, CiAiRepositoryGateEvaluation>());
        Check(noEvidenceState == CiAiPipelineGateState.Waiting, "missing-provider-evidence-is-dormant-waiting");

        var invalid = CiAiRepositoryGateEvaluation.FromDevelopmentCapability(
            Binding(),
            new CiAiDevelopmentCapabilityEvidenceValidation(false, "devcap.provider-job-not-successful", null));
        var invalidState = CiAiPipelineGateEvaluator.Evaluate(
            candidateExpression,
            new Dictionary<string, CiAiRepositoryGateEvaluation> { [GateId] = invalid });
        Check(invalidState == CiAiPipelineGateState.Blocked, "invalid-terminal-provider-evidence-blocks");

        var wrongSubject = new CiAiRepositoryGateEvaluation(
            Provider, CandidateId, SourceSha,
            CiAiRepositoryGateEvidenceKind.DevelopmentCapability,
            "physics.other-capability.v1", "1",
            CiAiPipelineGateState.Satisfied);
        Check(CiAiPipelineGateEvaluator.Evaluate(
            candidateExpression,
            new Dictionary<string, CiAiRepositoryGateEvaluation> { [GateId] = wrongSubject }) ==
            CiAiPipelineGateState.Blocked, "capability-id-mismatch-blocks");

        var wrongKind = new CiAiRepositoryGateEvaluation(
            Provider, CandidateId, SourceSha,
            CiAiRepositoryGateEvidenceKind.PublishedPackage,
            "physics.kinematics.prescribed-motion.v1", "1",
            CiAiPipelineGateState.Satisfied);
        Check(CiAiPipelineGateEvaluator.Evaluate(
            candidateExpression,
            new Dictionary<string, CiAiRepositoryGateEvaluation> { [GateId] = wrongKind }) ==
            CiAiPipelineGateState.Blocked, "published-package-evidence-cannot-satisfy-development-capability-leaf");

        var wrongVersionLeaf = new CiAiRepositoryGateReference(
            GateId, Provider, CandidateId, SourceSha,
            CiAiRepositoryGateEvidenceKind.DevelopmentCapability,
            CapabilityId, "2");
        var wrongVersionExpression = CiAiPipelineGateExpression.ForPipeline("raWWar-wrong-version", wrongVersionLeaf);
        Check(CiAiPipelineGateEvaluator.Evaluate(
            wrongVersionExpression,
            new Dictionary<string, CiAiRepositoryGateEvaluation> { [GateId] = candidateEvaluation }) ==
            CiAiPipelineGateState.Blocked, "capability-version-mismatch-blocks");

        return passed;
    }

    private static CiAiRepositoryGateReference Leaf(bool requiresProtectedPush) =>
        new(
            GateId,
            Provider,
            CandidateId,
            SourceSha,
            CiAiRepositoryGateEvidenceKind.DevelopmentCapability,
            CapabilityId,
            "1",
            requiresProtectedPush);

    private static CiAiDevelopmentCapabilityArtifactBinding Binding(bool protectedPush = false)
    {
        var sourceRef = protectedPush ? "refs/heads/development" : "refs/heads/adp/development-capability-evidence";
        var eventName = protectedPush ? "push" : "pull_request";
        var eventRef = protectedPush ? sourceRef : "refs/pull/64/merge";
        var eventSha = protectedPush ? SourceSha : EventSha;
        var workflowRef = "TrentBest/TheSingularityWorkshop.Physics/.github/workflows/build.yml@" + eventRef;
        var workflowSha = protectedPush ? SourceSha : WorkflowSha;
        var dependencies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TrentBest/TheSingularityWorkshop.MicroBundleDomain"] = "4444444444444444444444444444444444444444",
            ["TrentBest/TheSingularityWorkshop.Experiences"] = "5555555555555555555555555555555555555555"
        };
        return new CiAiDevelopmentCapabilityArtifactBinding(
            Provider, CapabilityId, "1", sourceRef, SourceSha,
            ".github/workflows/build.yml", workflowRef, workflowSha,
            1001, 2, eventName, eventRef, eventSha,
            sourceRef, SourceSha, "test", 77, "success",
            "physics-development-capability-1001-2", 88, "sha256:" + new string('c', 64), 4096, false,
            "physics.development-capability", 1, 5203, "0.1.0-alpha.1",
            dependencies,
            new[] { "Physics/Kinematics/PrescribedKinematicMotion.cs", "TheSingularityWorkshop.Physics.MicroBundles.csproj" },
            new[] { "validate_microbundles", "build_microbundles", "test_microbundles", "package_microbundles", "test_physics_solution", "package_core", "package_adapter" },
            new[] { "microbundle", "microbundleTests", "physicsTests", "workflowProvenance" },
            true);
    }

    private static CiAiDevelopmentCapabilityEvidence Evidence(bool isProtectedPush) =>
        new(
            Provider,
            CapabilityId,
            "1",
            isProtectedPush ? "refs/heads/development" : "refs/heads/adp/development-capability-evidence",
            SourceSha,
            ".github/workflows/build.yml",
            "TrentBest/TheSingularityWorkshop.Physics/.github/workflows/build.yml@refs/heads/development",
            WorkflowSha,
            1001,
            2,
            isProtectedPush ? "push" : "pull_request",
            isProtectedPush ? "refs/heads/development" : "refs/pull/64/merge",
            isProtectedPush ? SourceSha : EventSha,
            isProtectedPush ? "refs/heads/development" : "refs/heads/adp/development-capability-evidence",
            SourceSha,
            "test",
            77,
            "physics-development-capability-1001-2",
            88,
            "sha256:" + new string('c', 64),
            4096,
            "physics.development-capability",
            1,
            5203,
            "0.1.0-alpha.1",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TrentBest/TheSingularityWorkshop.MicroBundleDomain"] = "4444444444444444444444444444444444444444",
                ["TrentBest/TheSingularityWorkshop.Experiences"] = "5555555555555555555555555555555555555555"
            },
            new[]
            {
                new CiAiDevelopmentCapabilityTestArtifact("microbundle", "physics-microbundle-test-results-1001-2", new string('a', 64), 1024, new string('b', 64)),
                new CiAiDevelopmentCapabilityTestArtifact("physicsSolution", "physics-solution-test-results-1001-2", new string('a', 64), 2048, new string('b', 64))
            },
            isProtectedPush);
}
