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
        Check(valid.Evidence is { IsProtectedPushEvidence: false }, "pull-request-evidence-is-candidate-only");
        Check(valid.Evidence?.ArtifactDigest == "sha256:" + new string('c', 64), "provider-api-artifact-digest-is-retained");

        var protectedPush = CiAiDevelopmentCapabilityEvidenceParser.ParseAndValidate(
            Payload(protectedPush: true),
            Binding(protectedPush: true));
        Check(protectedPush.IsValid && protectedPush.Evidence is { IsProtectedPushEvidence: true },
            "exact-protected-development-push-is-distinguished");

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

    private static string? Error(byte[] payload, CiAiDevelopmentCapabilityArtifactBinding binding) =>
        CiAiDevelopmentCapabilityEvidenceParser.ParseAndValidate(payload, binding).ErrorCode;

    private static byte[] Mutate(byte[] payload, Action<JsonObject> mutation)
    {
        var root = JsonNode.Parse(payload)!.AsObject();
        mutation(root);
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    private static byte[] Payload(bool protectedPush = false)
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
                id = CapabilityId,
                version = CapabilityVersion,
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
            requiredJobs = new[] { new { name = "test", workflowPath = WorkflowPath } },
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
        string providerJobConclusion = "success")
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
            CapabilityId,
            CapabilityVersion,
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
