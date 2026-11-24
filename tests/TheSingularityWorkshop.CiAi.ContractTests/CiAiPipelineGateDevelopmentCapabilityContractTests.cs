namespace TheSingularityWorkshop.CiAi.ContractTests;

public static class CiAiPipelineGateDevelopmentCapabilityContractTests
{
    private const string Provider = "TrentBest/TheSingularityWorkshop.Physics";
    private const string CapabilityId = "physics.kinematics.prescribed-motion.v1";
    private const string SourceSha = "1111111111111111111111111111111111111111";
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
        var packageExpression = CiAiPipelineGateExpression.ForPipeline(
            "raWWar",
            new CiAiRepositoryGateReference(GateId, Provider, CandidateId, SourceSha));
        Check(candidateExpression.DefinitionSha256 != packageExpression.DefinitionSha256,
            "evidence-family-is-bound-into-composite-expression-identity");
        var (candidateBinding, candidateValidation) =
            CiAiDevelopmentCapabilityEvidenceContractTests.CreateFullyVerifiedForGateTests();
        var candidateEvaluation = CiAiRepositoryGateEvaluation.FromDevelopmentCapability(candidateBinding, candidateValidation);
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

        var (protectedBinding, protectedValidation) =
            CiAiDevelopmentCapabilityEvidenceContractTests.CreateFullyVerifiedForGateTests(protectedPush: true);
        var protectedEvaluation = CiAiRepositoryGateEvaluation.FromDevelopmentCapability(protectedBinding, protectedValidation);
        var protectedState = CiAiPipelineGateEvaluator.Evaluate(
            protectedExpression,
            new Dictionary<string, CiAiRepositoryGateEvaluation> { [GateId] = protectedEvaluation });
        Check(protectedState == CiAiPipelineGateState.Satisfied, "verified-protected-push-satisfies-protected-capability-gate");

        var noEvidenceState = CiAiPipelineGateEvaluator.Evaluate(
            candidateExpression,
            new Dictionary<string, CiAiRepositoryGateEvaluation>());
        Check(noEvidenceState == CiAiPipelineGateState.Waiting, "missing-provider-evidence-is-dormant-waiting");

        var invalidBinding = CiAiDevelopmentCapabilityEvidenceContractTests.CreateBindingForGateTests(providerJobConclusion: "failure");
        var invalidValidation = CiAiDevelopmentCapabilityEvidenceContractTests.ParseValidationForGateTests(providerJobConclusion: "failure");
        var invalid = CiAiRepositoryGateEvaluation.FromDevelopmentCapability(invalidBinding, invalidValidation);
        var invalidState = CiAiPipelineGateEvaluator.Evaluate(
            candidateExpression,
            new Dictionary<string, CiAiRepositoryGateEvaluation> { [GateId] = invalid });
        Check(invalidState == CiAiPipelineGateState.Blocked, "invalid-terminal-provider-evidence-blocks");

        var (wrongSubjectBinding, wrongSubjectValidation) =
            CiAiDevelopmentCapabilityEvidenceContractTests.CreateFullyVerifiedForGateTests(
                capabilityId: "physics.other-capability.v1");
        var wrongSubject = CiAiRepositoryGateEvaluation.FromDevelopmentCapability(wrongSubjectBinding, wrongSubjectValidation);
        Check(CiAiPipelineGateEvaluator.Evaluate(
            candidateExpression,
            new Dictionary<string, CiAiRepositoryGateEvaluation> { [GateId] = wrongSubject }) ==
            CiAiPipelineGateState.Blocked, "capability-id-mismatch-blocks");

        var publishedPackage = new DependencyReleaseGateEvaluation(
            CandidateId,
            Provider,
            SourceSha,
            "TheSingularityWorkshop.Physics",
            "0.1.0",
            DependencyReleaseGateState.ReadyForConsumerVerification,
            Array.Empty<string>(),
            Array.Empty<string>());
        var wrongKind = CiAiRepositoryGateEvaluation.FromPublishedDependency(publishedPackage);
        Check(CiAiPipelineGateEvaluator.Evaluate(
            candidateExpression,
            new Dictionary<string, CiAiRepositoryGateEvaluation> { [GateId] = wrongKind }) ==
            CiAiPipelineGateState.Blocked, "published-package-evidence-cannot-satisfy-development-capability-leaf");

        var (wrongVersionBinding, wrongVersionValidation) =
            CiAiDevelopmentCapabilityEvidenceContractTests.CreateFullyVerifiedForGateTests(capabilityVersion: "2");
        var wrongVersion = CiAiRepositoryGateEvaluation.FromDevelopmentCapability(wrongVersionBinding, wrongVersionValidation);
        var wrongVersionExpression = CiAiPipelineGateExpression.ForPipeline(
            "raWWar-wrong-version",
            new CiAiRepositoryGateReference(
                GateId, Provider, CandidateId, SourceSha,
                CiAiRepositoryGateEvidenceKind.DevelopmentCapability,
                CapabilityId, "1"));
        Check(CiAiPipelineGateEvaluator.Evaluate(
            wrongVersionExpression,
            new Dictionary<string, CiAiRepositoryGateEvaluation> { [GateId] = wrongVersion }) ==
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
}
