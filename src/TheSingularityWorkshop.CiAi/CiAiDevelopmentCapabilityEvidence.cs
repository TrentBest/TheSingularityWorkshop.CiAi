using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Exact GitHub API metadata and policy inputs required to validate one development-capability artifact.</summary>
public sealed record CiAiDevelopmentCapabilityArtifactBinding(
    string ProviderRepository,
    string CapabilityId,
    string CapabilityVersion,
    string SourceRef,
    string SourceSha,
    string WorkflowPath,
    string WorkflowRef,
    string WorkflowSha,
    long WorkflowRunId,
    int WorkflowAttempt,
    string EventName,
    string EventRef,
    string EventSha,
    string CheckoutRef,
    string CheckoutSha,
    string ProviderJobName,
    string ProviderJobKey,
    long ProviderJobId,
    string ProviderJobConclusion,
    string ArtifactName,
    long ProviderArtifactId,
    string ArtifactDigest,
    long ArtifactSizeBytes,
    bool ArtifactExpired,
    string PolicyId,
    int PolicyVersion,
    long BundleId,
    string BundleVersion,
    string BundleFile,
    IReadOnlyDictionary<string, string> ExpectedTestResultArtifacts,
    IReadOnlyDictionary<string, string> ExpectedDependencyCheckouts,
    IReadOnlyList<string> ExpectedSourcePaths,
    IReadOnlyList<string> RequiredStepIds,
    IReadOnlyList<string> RequiredArtifactKinds,
    bool IsProtectedDevelopmentBranch);

/// <summary>One exact test-result archive declared by the capability envelope.</summary>
public sealed record CiAiDevelopmentCapabilityTestArtifact(
    string Kind,
    string ArtifactName,
    string TrxSha256,
    long TrxSizeBytes,
    string ProvenanceSha256);

/// <summary>Verified development capability evidence. The artifact digest and job identity come from GitHub API metadata.</summary>
public sealed record CiAiDevelopmentCapabilityEvidence(
    string ProviderRepository,
    string CapabilityId,
    string CapabilityVersion,
    string SourceRef,
    string SourceSha,
    string WorkflowPath,
    string WorkflowRef,
    string WorkflowSha,
    long WorkflowRunId,
    int WorkflowAttempt,
    string EventName,
    string EventRef,
    string EventSha,
    string CheckoutRef,
    string CheckoutSha,
    string ProviderJobName,
    long ProviderJobId,
    string ArtifactName,
    long ProviderArtifactId,
    string ArtifactDigest,
    long ArtifactSizeBytes,
    string PolicyId,
    int PolicyVersion,
    long BundleId,
    string BundleVersion,
    string BundleFile,
    IReadOnlyDictionary<string, string> DependencyCheckouts,
    IReadOnlyList<CiAiDevelopmentCapabilityTestArtifact> TestArtifacts,
    bool IsProtectedPushEvidence);

/// <summary>Fail-closed result for parsing and validating a versioned development-capability evidence envelope.</summary>
public sealed record CiAiDevelopmentCapabilityEvidenceValidation
{
    internal CiAiDevelopmentCapabilityEvidenceValidation(
        bool isEnvelopeValid,
        bool testResultsVerified,
        string? errorCode,
        CiAiDevelopmentCapabilityEvidence? evidence)
    {
        IsEnvelopeValid = isEnvelopeValid;
        TestResultsVerified = testResultsVerified;
        ErrorCode = errorCode;
        Evidence = evidence;
    }

    public bool IsEnvelopeValid { get; }
    public bool TestResultsVerified { get; }
    public bool IsValid => IsEnvelopeValid && TestResultsVerified;
    public string? ErrorCode { get; }
    public CiAiDevelopmentCapabilityEvidence? Evidence { get; }
}

/// <summary>
/// Validates the provider-generated development-capability envelope against independently observed GitHub
/// workflow-run, job, artifact, source, dependency, and policy metadata. The JSON payload is untrusted.
/// </summary>
public static class CiAiDevelopmentCapabilityEvidenceParser
{
    public const string Schema = "ciai.development-capability-evidence.v1";
    public const int DefaultMaximumPayloadBytes = 1024 * 1024;

    public static CiAiDevelopmentCapabilityEvidenceValidation ParseAndValidate(
        ReadOnlyMemory<byte> payload,
        CiAiDevelopmentCapabilityArtifactBinding binding,
        int maximumPayloadBytes = DefaultMaximumPayloadBytes)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (maximumPayloadBytes <= 0 || maximumPayloadBytes > DefaultMaximumPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumPayloadBytes));
        if (payload.IsEmpty || payload.Length > maximumPayloadBytes)
            return Invalid("devcap.payload-size-invalid");

        try
        {
            ValidateBinding(binding);
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32
            });
            var root = document.RootElement;
            Ensure(root.ValueKind == JsonValueKind.Object, "devcap.root-invalid");
            Ensure(ReadString(root, "schema") == Schema, "devcap.schema-mismatch");
            Ensure(ReadString(root, "evidenceContract") == Schema, "devcap.contract-mismatch");
            Ensure(ReadString(root, "providerRepository") == binding.ProviderRepository, "devcap.provider-repository-mismatch");
            Ensure(ReadString(root, "artifactName") == binding.ArtifactName, "devcap.artifact-name-mismatch");

            var capability = ReadObject(root, "capability");
            Ensure(ReadString(capability, "id") == binding.CapabilityId, "devcap.capability-id-mismatch");
            Ensure(ReadString(capability, "version") == binding.CapabilityVersion, "devcap.capability-version-mismatch");
            Ensure(ReadString(capability, "channel") == "development", "devcap.channel-invalid");
            Ensure(ReadString(capability, "status") == "satisfied", "devcap.capability-not-satisfied");

            var candidate = ReadObject(root, "candidate");
            Ensure(ReadString(candidate, "id") == binding.ProviderRepository + "@" + binding.SourceSha, "devcap.candidate-id-mismatch");
            Ensure(ReadString(candidate, "sourceRef") == binding.SourceRef, "devcap.source-ref-mismatch");
            Ensure(ReadGitSha(candidate, "sourceSha") == binding.SourceSha, "devcap.source-sha-mismatch");

            var workflow = ReadObject(root, "workflow");
            Ensure(ReadString(workflow, "path") == binding.WorkflowPath, "devcap.workflow-path-mismatch");
            Ensure(ReadString(workflow, "ref") == binding.WorkflowRef, "devcap.workflow-ref-mismatch");
            Ensure(ReadGitSha(workflow, "sha") == binding.WorkflowSha, "devcap.workflow-sha-mismatch");
            Ensure(ReadInt64(workflow, "runId") == binding.WorkflowRunId, "devcap.run-id-mismatch");
            Ensure(ReadInt32(workflow, "runAttempt") == binding.WorkflowAttempt, "devcap.run-attempt-mismatch");
            Ensure(ReadString(workflow, "eventName") == binding.EventName, "devcap.event-name-mismatch");
            Ensure(ReadString(workflow, "eventRef") == binding.EventRef, "devcap.event-ref-mismatch");
            Ensure(ReadGitSha(workflow, "eventSha") == binding.EventSha, "devcap.event-sha-mismatch");

            var checkout = ReadObject(root, "checkout");
            Ensure(ReadString(checkout, "ref") == binding.CheckoutRef, "devcap.checkout-ref-mismatch");
            Ensure(ReadGitSha(checkout, "sha") == binding.CheckoutSha, "devcap.checkout-sha-mismatch");
            Ensure(binding.CheckoutSha == binding.SourceSha, "devcap.checkout-not-candidate-sha");

            var dependencyCheckouts = ReadDependencies(checkout);
            Ensure(DictionaryEquals(dependencyCheckouts, binding.ExpectedDependencyCheckouts), "devcap.dependency-checkout-mismatch");

            var requiredJobs = ReadArray(root, "requiredJobs");
            Ensure(requiredJobs.Length == 1, "devcap.required-jobs-invalid");
            Ensure(ReadString(requiredJobs[0], "name") == binding.ProviderJobName, "devcap.job-name-mismatch");
            Ensure(ReadString(requiredJobs[0], "jobKey") == binding.ProviderJobKey, "devcap.job-key-mismatch");
            Ensure(ReadString(requiredJobs[0], "workflowPath") == binding.WorkflowPath, "devcap.job-workflow-mismatch");
            Ensure(!string.IsNullOrWhiteSpace(binding.ProviderJobName) && !string.IsNullOrWhiteSpace(binding.ProviderJobKey),
            "devcap.expected-provider-job-identity-missing");
        Ensure(binding.ProviderJobConclusion == "success", "devcap.provider-job-not-successful");
            Ensure(binding.ProviderJobId > 0, "devcap.provider-job-id-invalid");

            var requiredStepIds = ReadStringArray(root, "requiredStepIds");
            Ensure(SetEquals(requiredStepIds, binding.RequiredStepIds), "devcap.required-step-set-mismatch");
            var outcomes = ReadObject(root, "stepOutcomes");
            foreach (var stepId in binding.RequiredStepIds)
                Ensure(ReadString(outcomes, stepId) == "success", "devcap.required-step-not-successful:" + stepId);

            var requiredArtifacts = ReadStringArray(root, "requiredArtifacts");
            Ensure(SetEquals(requiredArtifacts, binding.RequiredArtifactKinds), "devcap.required-artifact-set-mismatch");
            Ensure(ReadBoolean(root, "artifactsComplete"), "devcap.artifacts-incomplete");
            Ensure(ReadBoolean(root, "identityValid"), "devcap.identity-invalid");
            Ensure(ReadBoolean(root, "sourceFilesComplete"), "devcap.source-files-incomplete");
            Ensure(ReadBoolean(root, "bundleIntegrityValid"), "devcap.bundle-integrity-invalid");
            Ensure(ReadBoolean(root, "satisfied"), "devcap.satisfied-flag-false");

            var artifacts = ReadArray(root, "artifacts");
            ValidateArtifacts(artifacts, binding.RequiredArtifactKinds);
            var sourceFiles = ReadArray(root, "sourceFiles");
            ValidateSourceFiles(sourceFiles, binding.ExpectedSourcePaths);

            var testArtifacts = ValidateTestArtifacts(
                ReadArray(root, "testResultArtifacts"),
                binding.ExpectedTestResultArtifacts);

            var microBundle = ReadObject(root, "microBundle");
            Ensure(ReadInt64(microBundle, "bundleId") == binding.BundleId, "devcap.bundle-id-mismatch");
            Ensure(ReadString(microBundle, "version") == binding.BundleVersion, "devcap.bundle-version-mismatch");
            Ensure(ReadString(microBundle, "file") == binding.BundleFile, "devcap.bundle-file-mismatch");
            Ensure(!ReadBoolean(microBundle, "isNuGetPackage"), "devcap.bundle-misclassified-as-nuget");
            Ensure(!ReadBoolean(microBundle, "isPublished"), "devcap.development-capability-published");

            var policy = ReadObject(root, "policy");
            Ensure(ReadString(policy, "id") == binding.PolicyId, "devcap.policy-id-mismatch");
            Ensure(ReadInt32(policy, "version") == binding.PolicyVersion, "devcap.policy-version-mismatch");

            Ensure(ReadString(root, "status") == "satisfied", "devcap.status-not-satisfied");
            Ensure(RepositoryIdentity.IsValid(binding.ProviderRepository), "devcap.expected-provider-invalid");
            Ensure(IsSha(binding.ArtifactDigest), "devcap.provider-artifact-digest-invalid");
            Ensure(binding.ArtifactSizeBytes > 0, "devcap.provider-artifact-size-invalid");
            Ensure(!binding.ArtifactExpired, "devcap.provider-artifact-expired");

            var protectedPush =
                binding.IsProtectedDevelopmentBranch &&
                binding.SourceRef == "refs/heads/development" &&
                binding.EventName == "push" &&
                binding.EventRef == binding.SourceRef &&
                binding.EventSha == binding.SourceSha &&
                binding.WorkflowRef.EndsWith("@refs/heads/development", StringComparison.Ordinal) &&
                binding.WorkflowSha == binding.SourceSha &&
                binding.CheckoutRef == binding.SourceRef;

            var evidence = new CiAiDevelopmentCapabilityEvidence(
                binding.ProviderRepository,
                binding.CapabilityId,
                binding.CapabilityVersion,
                binding.SourceRef,
                binding.SourceSha,
                binding.WorkflowPath,
                binding.WorkflowRef,
                binding.WorkflowSha,
                binding.WorkflowRunId,
                binding.WorkflowAttempt,
                binding.EventName,
                binding.EventRef,
                binding.EventSha,
                binding.CheckoutRef,
                binding.CheckoutSha,
                binding.ProviderJobName,
                binding.ProviderJobKey,
                binding.ProviderJobId,
                binding.ArtifactName,
                binding.ProviderArtifactId,
                binding.ArtifactDigest,
                binding.ArtifactSizeBytes,
                binding.PolicyId,
                binding.PolicyVersion,
                binding.BundleId,
                binding.BundleVersion,
                binding.BundleFile,
                dependencyCheckouts,
                testArtifacts,
                protectedPush);
            return new CiAiDevelopmentCapabilityEvidenceValidation(true, false, null, evidence);
        }
        catch (InvalidDataException exception)
        {
            return Invalid(exception.Message);
        }
        catch (JsonException)
        {
            return Invalid("devcap.json-invalid");
        }
        catch (InvalidOperationException)
        {
            return Invalid("devcap.json-shape-invalid");
        }
    }

    private static void ValidateBinding(CiAiDevelopmentCapabilityArtifactBinding binding)
    {
        Ensure(RepositoryIdentity.IsValid(binding.ProviderRepository), "devcap.expected-provider-invalid");
        Ensure(IsGitSha(binding.SourceSha) && IsGitSha(binding.WorkflowSha) && IsGitSha(binding.EventSha) && IsGitSha(binding.CheckoutSha),
            "devcap.expected-sha-invalid");
        Ensure(binding.WorkflowRunId > 0 && binding.WorkflowAttempt > 0, "devcap.expected-run-identity-invalid");
        Ensure(binding.ProviderArtifactId > 0 && binding.ProviderJobId > 0, "devcap.expected-provider-object-id-invalid");
        Ensure(binding.ArtifactSizeBytes > 0 && IsProviderArtifactDigest(binding.ArtifactDigest), "devcap.expected-artifact-metadata-invalid");
        Ensure(!binding.ArtifactExpired, "devcap.provider-artifact-expired");
        Ensure(binding.ProviderJobConclusion == "success", "devcap.provider-job-not-successful");
        Ensure(binding.SourceRef.StartsWith("refs/heads/", StringComparison.Ordinal), "devcap.expected-source-ref-invalid");
        Ensure(binding.CheckoutRef == binding.SourceRef, "devcap.expected-checkout-ref-mismatch");
        Ensure(binding.ExpectedTestResultArtifacts is not null && binding.ExpectedTestResultArtifacts.Count > 0,
            "devcap.expected-test-artifacts-missing");
        Ensure(!string.IsNullOrWhiteSpace(binding.BundleFile), "devcap.expected-bundle-file-missing");
        Ensure(binding.ExpectedDependencyCheckouts is not null && binding.ExpectedDependencyCheckouts.Count > 0,
            "devcap.expected-dependencies-missing");
        Ensure(binding.ExpectedSourcePaths is not null && binding.ExpectedSourcePaths.Count > 0,
            "devcap.expected-source-paths-missing");
        Ensure(binding.RequiredStepIds is not null && binding.RequiredStepIds.Count > 0,
            "devcap.required-step-policy-missing");
        Ensure(binding.RequiredArtifactKinds is not null && binding.RequiredArtifactKinds.Count > 0,
            "devcap.required-artifact-policy-missing");
    }

    private static Dictionary<string, string> ReadDependencies(JsonElement checkout)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in ReadArray(checkout, "dependencies"))
        {
            var repository = ReadString(item, "repository");
            var sha = ReadGitSha(item, "checkoutSha");
            Ensure(RepositoryIdentity.IsValid(repository), "devcap.dependency-repository-invalid");
            Ensure(result.TryAdd(repository, sha), "devcap.dependency-duplicate");
        }
        return result;
    }

    private static void ValidateArtifacts(JsonElement[] artifacts, IReadOnlyList<string> requiredKinds)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in artifacts)
        {
            var kind = ReadString(item, "kind");
            Ensure(found.Add(kind), "devcap.artifact-kind-duplicate");
            _ = ReadString(item, "path");
            Ensure(IsSha256(ReadString(item, "sha256")), "devcap.artifact-digest-invalid:" + kind);
            Ensure(ReadInt64(item, "sizeBytes") > 0, "devcap.artifact-size-invalid:" + kind);
        }
        Ensure(SetEquals(found, requiredKinds), "devcap.artifact-kind-set-mismatch");
    }

    private static void ValidateSourceFiles(JsonElement[] files, IReadOnlyList<string> expectedPaths)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in files)
        {
            var path = ReadString(item, "path");
            Ensure(found.Add(path), "devcap.source-file-duplicate");
            Ensure(IsSha256(ReadString(item, "sha256")), "devcap.source-file-digest-invalid");
            Ensure(ReadInt64(item, "sizeBytes") > 0, "devcap.source-file-size-invalid");
        }
        Ensure(SetEquals(found, expectedPaths), "devcap.source-file-set-mismatch");
    }

    private static IReadOnlyList<CiAiDevelopmentCapabilityTestArtifact> ValidateTestArtifacts(
        JsonElement[] artifacts,
        IReadOnlyDictionary<string, string> expectedArtifacts)
    {
        Ensure(artifacts.Length == expectedArtifacts.Count, "devcap.test-artifact-count-invalid");
        var results = new List<CiAiDevelopmentCapabilityTestArtifact>(artifacts.Length);
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in artifacts)
        {
            var kind = ReadString(item, "kind");
            if (!expectedArtifacts.TryGetValue(kind, out var artifactName))
                throw new InvalidDataException("devcap.test-artifact-kind-invalid");
            Ensure(found.Add(kind), "devcap.test-artifact-kind-duplicate");
            Ensure(ReadString(item, "artifactName") == artifactName, "devcap.test-artifact-name-mismatch");
            var entries = ReadStringArray(item, "archiveEntries");
            Ensure(SetEquals(entries, new[] { "results.trx", "provenance.json" }), "devcap.test-archive-entry-set-invalid");
            var trxSha = ReadString(item, "trxSha256");
            var provenanceSha = ReadString(item, "provenanceSha256");
            Ensure(IsSha256(trxSha) && IsSha256(provenanceSha), "devcap.test-artifact-digest-invalid");
            var trxSize = ReadInt64(item, "trxSizeBytes");
            Ensure(trxSize > 0, "devcap.test-artifact-size-invalid");
            results.Add(new CiAiDevelopmentCapabilityTestArtifact(kind, artifactName, trxSha, trxSize, provenanceSha));
        }
        Ensure(found.Count == expectedArtifacts.Count, "devcap.test-artifact-set-invalid");
        return results.AsReadOnly();
    }

    private static JsonElement ReadObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("devcap.object-missing:" + name);
        return value;
    }

    private static JsonElement[] ReadArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("devcap.array-missing:" + name);
        return value.EnumerateArray().ToArray();
    }

    private static string[] ReadStringArray(JsonElement parent, string name)
    {
        var values = ReadArray(parent, name);
        return values.Select(value =>
        {
            Ensure(value.ValueKind == JsonValueKind.String, "devcap.array-string-invalid:" + name);
            return value.GetString()!;
        }).ToArray();
    }

    private static string ReadString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("devcap.string-missing:" + name);
        var result = value.GetString()!;
        Ensure(result.Length is > 0 and <= 1024 && !string.IsNullOrWhiteSpace(result), "devcap.string-length-invalid:" + name);
        return result;
    }

    private static string ReadGitSha(JsonElement parent, string name)
    {
        var value = ReadString(parent, name);
        Ensure(IsGitSha(value), "devcap.sha-invalid:" + name);
        return value.ToLowerInvariant();
    }

    private static long ReadInt64(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt64(out var number))
            throw new InvalidDataException("devcap.integer-missing:" + name);
        return number;
    }

    private static int ReadInt32(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var number))
            throw new InvalidDataException("devcap.integer-missing:" + name);
        return number;
    }

    private static bool ReadBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
            throw new InvalidDataException("devcap.boolean-missing:" + name);
        return value.GetBoolean();
    }

    private static void EnsureNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                Ensure(names.Add(property.Name), "devcap.json-duplicate-property");
                EnsureNoDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                EnsureNoDuplicateProperties(item);
        }
    }

    private static bool DictionaryEquals(
        IReadOnlyDictionary<string, string> actual,
        IReadOnlyDictionary<string, string> expected)
    {
        if (actual.Count != expected.Count) return false;
        foreach (var pair in expected)
            if (!actual.TryGetValue(pair.Key, out var value) ||
                !string.Equals(value, pair.Value, StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    private static bool SetEquals(IEnumerable<string> actual, IEnumerable<string> expected) =>
        new HashSet<string>(actual, StringComparer.Ordinal).SetEquals(expected);

    private static bool IsGitSha(string value) =>
        value.Length == 40 && value.All(Uri.IsHexDigit);

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool IsProviderArtifactDigest(string value) =>
        value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && IsSha256(value[7..]);

    private static void Ensure(bool condition, string errorCode)
    {
        if (!condition) throw new InvalidDataException(errorCode);
    }

    private static CiAiDevelopmentCapabilityEvidenceValidation Invalid(string errorCode) =>
        new(false, false, errorCode, null);
}
