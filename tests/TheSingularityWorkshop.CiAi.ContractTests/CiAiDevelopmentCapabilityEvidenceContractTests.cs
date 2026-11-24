using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TheSingularityWorkshop.CiAi.ContractTests;

public static class CiAiDevelopmentCapabilityEvidenceContractTests
{
    private const string Provider = "TrentBest/TheSingularityWorkshop.Physics";
    private const string CapabilityId = "physics.kinematics.prescribed-motion.v1";
    private const string CapabilityVersion = "1";
    private const string SourceSha = "1111111111111111111111111111111111111111";
    private const string WorkflowSha = "2222222222222222222222222222222222222222";
    private const string EventSha = "3333333333333333333333333333333333333333";
    private const string MicroBundleDomainSha = "4444444444444444444444444444444444444444";
    private const string ExperiencesSha = "5555555555555555555555555555555555555555";
    private const string SourceRef = "refs/heads/adp/development-capability-evidence";
    private const string WorkflowPath = ".github/workflows/build.yml";
    private const string WorkflowRef = "TrentBest/TheSingularityWorkshop.Physics/.github/workflows/build.yml@refs/pull/64/merge";
    private const string EventRef = "refs/pull/64/merge";
    private const string BundleVersion = "0.1.0-alpha.1";
    private const long RunId = 1001;
    private const int Attempt = 2;

    public static int Run()
    {
        var passed = 0;
        void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("CiAiDevelopmentCapabilityEvidenceContractTests failed: " + name);
            passed++;
        }

        var valid = CiAiDevelopmentCapabilityEvidenceParser.ParseAndValidate(Payload(), Binding());
        Check(valid.IsValid && valid.Evidence is not null, "exact-candidate-evidence-is-valid");
        Check(valid.Evidence?.ProviderJobKey == "physics-development-capability", "provider-job-key-is-retained");
        Check(valid.Evidence is { IsProtectedPushEvidence: false }, "pull-request-evidence-is-candidate-only");
        Check(valid.Evidence?.ArtifactDigest == "sha256:" + new string('c', 64), "provider-api-artifact-digest-is-retained");

        var envelopeOnlyGate = CiAiRepositoryGateEvaluation.FromDevelopmentCapability(Binding(), valid);
        Check(envelopeOnlyGate.State == CiAiPipelineGateState.Waiting,
            "valid-envelope-alone-remains-dormant-until-test-archives-are-verified");

        var fullyVerified = CreateFullyVerifiedForGateTests();
        Check(fullyVerified.Validation.IsValid && fullyVerified.Validation.TestResultsVerified,
            "capability-admission-requires-exact-trx-and-provenance-verification");

        var protectedPush = CiAiDevelopmentCapabilityEvidenceParser.ParseAndValidate(
            Payload(protectedPush: true),
            Binding(protectedPush: true));
        Check(protectedPush.IsEnvelopeValid && !protectedPush.TestResultsVerified &&
            protectedPush.Evidence is { IsProtectedPushEvidence: true },
            "exact-protected-development-push-is-distinguished-before-test-archive-verification");

        var wrongSource = Mutate(Payload(), root => root["candidate"]!["sourceSha"] = new string('9', 40));
        Check(Error(wrongSource, Binding()) == "devcap.source-sha-mismatch", "source-sha-mismatch-fails-closed");

        var failedStep = Mutate(Payload(), root => root["stepOutcomes"]!["test_physics_solution"] = "failure");
        Check(Error(failedStep, Binding()) == "devcap.required-step-not-successful:test_physics_solution",
            "required-test-failure-fails-closed");

        var wrongDependency = Mutate(Payload(), root =>
            root["checkout"]!["dependencies"]![0]!["checkoutSha"] = new string('9', 40));
        Check(Error(wrongDependency, Binding()) == "devcap.dependency-checkout-mismatch",
            "dependency-checkout-mismatch-fails-closed");

        Check(Error(Payload(), Binding(artifactExpired: true)) == "devcap.provider-artifact-expired",
            "expired-artifact-fails-closed");

        var wrongBundle = Mutate(Payload(), root => root["microBundle"]!["bundleId"] = 999);
        Check(Error(wrongBundle, Binding()) == "devcap.bundle-id-mismatch", "wrong-bundle-identity-fails-closed");

        var publishedBundle = Mutate(Payload(), root => root["microBundle"]!["isPublished"] = true);
        Check(Error(publishedBundle, Binding()) == "devcap.development-capability-published",
            "development-capability-cannot-be-mislabeled-as-published");

        var wrongArchiveName = Mutate(Payload(), root =>
            root["testResultArtifacts"]![0]!["artifactName"] = "wrong-artifact");
        Check(Error(wrongArchiveName, Binding()) == "devcap.test-artifact-name-mismatch",
            "test-result-artifact-is-bound-to-exact-run-and-attempt");

        Check(Error(Payload(), Binding(artifactDigest: "sha256:bad")) == "devcap.expected-artifact-metadata-invalid",
            "malformed-provider-artifact-digest-fails-closed");

        var duplicate = Encoding.UTF8.GetBytes("{\"schema\":\"ciai.development-capability-evidence.v1\",\"schema\":\"ciai.development-capability-evidence.v1\"}");
        Check(Error(duplicate, Binding()) == "devcap.json-duplicate-property",
            "duplicate-json-properties-are-rejected");

        var failedJob = CiAiDevelopmentCapabilityEvidenceParser.ParseAndValidate(
            Payload(),
            Binding(providerJobConclusion: "failure"));
        Check(!failedJob.IsValid && failedJob.ErrorCode == "devcap.provider-job-not-successful",
            "provider-job-conclusion-is-independently-bound");

        return passed;
    }

    internal static (CiAiDevelopmentCapabilityArtifactBinding Binding, CiAiDevelopmentCapabilityEvidenceValidation Validation)
        CreateFullyVerifiedForGateTests(
            bool protectedPush = false,
            string capabilityId = CapabilityId,
            string capabilityVersion = CapabilityVersion,
            bool testResultsPass = true)
    {
        var (report, archive) = testResultsPass
            ? CiTestResultReportTests.CreatePassingReportForDevelopmentCapability(protectedPush)
            : CiTestResultReportTests.CreateFailingReportForDevelopmentCapability(protectedPush);
        var sourceRef = protectedPush
            ? "refs/heads/development"
            : "refs/heads/feature/minesweeper-domain-pilot";
        var sourcePaths = new[] { "src/Capability.cs" };
        var requiredSteps = new[] { "build", "test" };
        var requiredKinds = new[] { "microbundle", "workflowProvenance" };
        var testArtifactNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["microbundle"] = report.SourceArtifactName!
        };
        var dependencies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TrentBest/ExampleDependency"] = new string('d', 40)
        };
        var artifactName = "test-development-capability-" + report.RunId + "-" + report.RunAttempt;
        var binding = new CiAiDevelopmentCapabilityArtifactBinding(
            report.Repository,
            capabilityId,
            capabilityVersion,
            sourceRef,
            report.CandidateSha,
            report.WorkflowPath,
            report.WorkflowDefinitionRef,
            report.WorkflowDefinitionSha,
            report.RunId,
            report.RunAttempt,
            report.EventName,
            report.EventRef,
            report.EventSha,
            report.CandidateRef,
            report.CandidateSha,
            report.JobName,
            report.SourceJobKey!,
            report.JobId,
            report.JobConclusion!,
            artifactName,
            9901,
            "sha256:" + new string('c', 64),
            4096,
            false,
            "test.development-capability",
            1,
            5203,
            "0.1.0-alpha.1",
            "test.mbundle",
            testArtifactNames,
            dependencies,
            sourcePaths,
            requiredSteps,
            requiredKinds,
            protectedPush);

        var digest = new string('a', 64);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = CiAiDevelopmentCapabilityEvidenceParser.Schema,
            evidenceContract = CiAiDevelopmentCapabilityEvidenceParser.Schema,
            providerRepository = report.Repository,
            artifactName,
            capability = new
            {
                id = capabilityId,
                version = capabilityVersion,
                channel = "development",
                status = "satisfied",
                implementationPaths = sourcePaths,
                validationProjects = new[] { "tests/Capability.Tests.csproj" }
            },
            candidate = new
            {
                id = report.Repository + "@" + report.CandidateSha,
                sourceRef,
                sourceSha = report.CandidateSha
            },
            workflow = new
            {
                path = report.WorkflowPath,
                @ref = report.WorkflowDefinitionRef,
                sha = report.WorkflowDefinitionSha,
                runId = report.RunId,
                runAttempt = report.RunAttempt,
                eventName = report.EventName,
                eventRef = report.EventRef,
                eventSha = report.EventSha
            },
            checkout = new
            {
                @ref = report.CandidateRef,
                sha = report.CandidateSha,
                dependencies = new[]
                {
                    new { repository = "TrentBest/ExampleDependency", checkoutSha = new string('d', 40) }
                }
            },
            pullRequest = protectedPush ? null : new
            {
                number = 17,
                head = new { @ref = "feature/minesweeper-domain-pilot", sha = report.CandidateSha },
                @base = new { @ref = "development", sha = report.WorkflowDefinitionSha }
            },
            requiredJobs = new[]
            {
                new { jobKey = report.SourceJobKey, name = report.JobName, workflowPath = report.WorkflowPath }
            },
            requiredStepIds = requiredSteps,
            stepOutcomes = requiredSteps.ToDictionary(name => name, _ => "success"),
            requiredArtifacts = requiredKinds,
            artifactsComplete = true,
            identityValid = true,
            sourceFilesComplete = true,
            bundleIntegrityValid = true,
            artifacts = new[]
            {
                new { kind = "microbundle", path = "test.mbundle", sha256 = digest, sizeBytes = 512 },
                new { kind = "workflowProvenance", path = "provenance.json", sha256 = archive.ProvenanceSha256, sizeBytes = 512 }
            },
            testResultArtifacts = new[]
            {
                new
                {
                    kind = "microbundle",
                    artifactName = report.SourceArtifactName,
                    trxSha256 = archive.TrxSha256,
                    trxSizeBytes = archive.TrxBytes.Length,
                    provenanceSha256 = archive.ProvenanceSha256,
                    archiveEntries = new[] { "results.trx", "provenance.json" }
                }
            },
            sourceFiles = new[] { new { path = sourcePaths[0], sha256 = digest, sizeBytes = 128 } },
            microBundle = new
            {
                bundleId = 5203,
                version = "0.1.0-alpha.1",
                file = "test.mbundle",
                isNuGetPackage = false,
                isPublished = false
            },
            policy = new { id = "test.development-capability", version = 1 },
            observedAtUtc = report.ObservedAt.ToString("O"),
            status = "satisfied",
            satisfied = true
        });
        var envelope = CiAiDevelopmentCapabilityEvidenceParser.ParseAndValidate(payload, binding);
        var verifiedTest = CiAiDevelopmentCapabilityVerifiedTestArtifact.FromVerifiedReport("microbundle", archive, report);
        var validation = CiAiDevelopmentCapabilityEvidenceVerifier.VerifyTestResults(
            binding,
            envelope,
            new Dictionary<string, CiAiDevelopmentCapabilityVerifiedTestArtifact>(StringComparer.Ordinal)
            {
                ["microbundle"] = verifiedTest
            });
        return (binding, validation);
    }

    internal static CiAiDevelopmentCapabilityArtifactBinding CreateBindingForGateTests(
        bool protectedPush = false,
        string capabilityId = CapabilityId,
        string capabilityVersion = CapabilityVersion,
        string providerJobConclusion = "success") =>
        Binding(
            protectedPush: protectedPush,
            providerJobConclusion: providerJobConclusion,
            capabilityId: capabilityId,
            capabilityVersion: capabilityVersion);

    internal static CiAiDevelopmentCapabilityEvidenceValidation ParseValidationForGateTests(
        bool protectedPush = false,
        string capabilityId = CapabilityId,
        string capabilityVersion = CapabilityVersion,
        string providerJobConclusion = "success")
    {
        var binding = CreateBindingForGateTests(protectedPush, capabilityId, capabilityVersion, providerJobConclusion);
        return CiAiDevelopmentCapabilityEvidenceParser.ParseAndValidate(
            Payload(protectedPush, capabilityId, capabilityVersion),
            binding);
    }

    private static string? Error(byte[] payload, CiAiDevelopmentCapabilityArtifactBinding binding) =>
        CiAiDevelopmentCapabilityEvidenceParser.ParseAndValidate(payload, binding).ErrorCode;

    private static byte[] Mutate(byte[] payload, Action<JsonObject> mutation)
    {
        var root = JsonNode.Parse(payload)!.AsObject();
        mutation(root);
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    private static byte[] Payload(bool protectedPush = false, string capabilityId = CapabilityId, string capabilityVersion = CapabilityVersion)
    {
        var sourceRef = protectedPush ? "refs/heads/development" : SourceRef;
        var eventName = protectedPush ? "push" : "pull_request";
        var eventRef = protectedPush ? sourceRef : EventRef;
        var eventSha = protectedPush ? SourceSha : EventSha;
        var workflowRef = protectedPush
            ? "TrentBest/TheSingularityWorkshop.Physics/.github/workflows/build.yml@refs/heads/development"
            : WorkflowRef;
        var workflowSha = protectedPush ? SourceSha : WorkflowSha;
        var dependencies = new[]
        {
            new { repository = "TrentBest/TheSingularityWorkshop.MicroBundleDomain", checkoutSha = MicroBundleDomainSha },
            new { repository = "TrentBest/TheSingularityWorkshop.Experiences", checkoutSha = ExperiencesSha }
        };
        var requiredSteps = RequiredSteps();
        var requiredArtifacts = RequiredArtifacts();
        var digest = new string('a', 64);
        var payload = new
        {
            schema = CiAiDevelopmentCapabilityEvidenceParser.Schema,
            evidenceContract = CiAiDevelopmentCapabilityEvidenceParser.Schema,
            providerRepository = Provider,
            artifactName = "physics-development-capability-" + RunId + "-" + Attempt,
            capability = new
            {
                id = capabilityId,
                version = capabilityVersion,
                channel = "development",
                status = "satisfied",
                implementationPaths = SourcePaths(),
                validationProjects = new[]
                {
                    "tests/MicroBundles/TheSingularityWorkshop.Physics.MicroBundles.Tests.csproj",
                    "TheSingularityWorkshop.Physics.sln"
                }
            },
            candidate = new
            {
                id = Provider + "@" + SourceSha,
                sourceRef,
                sourceSha = SourceSha
            },
            workflow = new
            {
                path = WorkflowPath,
                @ref = workflowRef,
                sha = workflowSha,
                runId = RunId,
                runAttempt = Attempt,
                eventName,
                eventRef,
                eventSha
            },
            checkout = new
            {
                @ref = sourceRef,
                sha = SourceSha,
                dependencies
            },
            pullRequest = protectedPush ? null : new
            {
                number = 64,
                head = new { @ref = "adp/development-capability-evidence", sha = SourceSha },
                @base = new { @ref = "development", sha = WorkflowSha }
            },
            requiredJobs = new[] { new { jobKey = "physics-development-capability", name = "test", workflowPath = WorkflowPath } },
            requiredStepIds = requiredSteps,
            stepOutcomes = requiredSteps.ToDictionary(name => name, _ => "success"),
            requiredArtifacts,
            artifactsComplete = true,
            identityValid = true,
            sourceFilesComplete = true,
            bundleIntegrityValid = true,
            artifacts = new[]
            {
                new { kind = "microbundle", path = "physics-prescribed-kinematic-motion.mbundle", sha256 = digest, sizeBytes = 512 },
                new { kind = "microbundleTests", path = "physics-microbundles.trx", sha256 = digest, sizeBytes = 1024 },
                new { kind = "physicsTests", path = "physics-tests.trx", sha256 = digest, sizeBytes = 2048 },
                new { kind = "workflowProvenance", path = "provenance.json", sha256 = digest, sizeBytes = 512 }
            },
            testResultArtifacts = new[]
            {
                new
                {
                    kind = "microbundle",
                    artifactName = "physics-microbundle-test-results-" + RunId + "-" + Attempt,
                    trxSha256 = digest,
                    trxSizeBytes = 1024,
                    provenanceSha256 = digest,
                    archiveEntries = new[] { "results.trx", "provenance.json" }
                },
                new
                {
                    kind = "physicsSolution",
                    artifactName = "physics-solution-test-results-" + RunId + "-" + Attempt,
                    trxSha256 = digest,
                    trxSizeBytes = 2048,
                    provenanceSha256 = digest,
                    archiveEntries = new[] { "results.trx", "provenance.json" }
                }
            },
            sourceFiles = SourcePaths().Select(path => new { path, sha256 = digest, sizeBytes = 128 }).ToArray(),
            microBundle = new
            {
                bundleId = 5203,
                version = BundleVersion,
                file = "physics-prescribed-kinematic-motion.mbundle",
                isNuGetPackage = false,
                isPublished = false
            },
            policy = new { id = "physics.development-capability", version = 1 },
            observedAtUtc = "2026-11-24T00:00:00Z",
            status = "satisfied",
            satisfied = true
        };
        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }

    private static CiAiDevelopmentCapabilityArtifactBinding Binding(
        bool protectedPush = false,
        bool artifactExpired = false,
        string? artifactDigest = null,
        string providerJobConclusion = "success",
        string capabilityId = CapabilityId,
        string capabilityVersion = CapabilityVersion)
    {
        var sourceRef = protectedPush ? "refs/heads/development" : SourceRef;
        var eventName = protectedPush ? "push" : "pull_request";
        var eventRef = protectedPush ? sourceRef : EventRef;
        var eventSha = protectedPush ? SourceSha : EventSha;
        var workflowRef = protectedPush
            ? "TrentBest/TheSingularityWorkshop.Physics/.github/workflows/build.yml@refs/heads/development"
            : WorkflowRef;
        var workflowSha = protectedPush ? SourceSha : WorkflowSha;
        var dependencies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TrentBest/TheSingularityWorkshop.MicroBundleDomain"] = MicroBundleDomainSha,
            ["TrentBest/TheSingularityWorkshop.Experiences"] = ExperiencesSha
        };
        return new CiAiDevelopmentCapabilityArtifactBinding(
            Provider,
            capabilityId,
            capabilityVersion,
            sourceRef,
            SourceSha,
            WorkflowPath,
            workflowRef,
            workflowSha,
            RunId,
            Attempt,
            eventName,
            eventRef,
            eventSha,
            sourceRef,
            SourceSha,
            "test",
            "physics-development-capability",
            77,
            providerJobConclusion,
            "physics-development-capability-" + RunId + "-" + Attempt,
            88,
            artifactDigest ?? "sha256:" + new string('c', 64),
            4096,
            artifactExpired,
            "physics.development-capability",
            1,
            5203,
            BundleVersion,
            "physics-prescribed-kinematic-motion.mbundle",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["microbundle"] = "physics-microbundle-test-results-" + RunId + "-" + Attempt,
                ["physicsSolution"] = "physics-solution-test-results-" + RunId + "-" + Attempt
            },
            dependencies,
            SourcePaths(),
            RequiredSteps(),
            RequiredArtifacts(),
            true);
    }

    private static string[] SourcePaths() =>
    [
        "Physics/Kinematics/PrescribedKinematicMotion.cs",
        "TheSingularityWorkshop.Physics.MicroBundles.csproj"
    ];

    private static string[] RequiredSteps() =>
    [
        "validate_microbundles",
        "build_microbundles",
        "test_microbundles",
        "package_microbundles",
        "test_physics_solution",
        "package_core",
        "package_adapter"
    ];

    private static string[] RequiredArtifacts() =>
    [
        "microbundle",
        "microbundleTests",
        "physicsTests",
        "workflowProvenance"
    ];
}
