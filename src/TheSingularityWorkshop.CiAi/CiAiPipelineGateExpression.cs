using System.Security.Cryptography;
using System.Text;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Three-valued result of evaluating a composite prerequisite expression.</summary>
public enum CiAiPipelineGateState
{
    Waiting = 0,
    Satisfied = 1,
    Blocked = 2
}

/// <summary>Evidence family used to satisfy one repository prerequisite.</summary>
public enum CiAiRepositoryGateEvidenceKind
{
    PublishedPackage = 0,
    DevelopmentCapability = 1
}

/// <summary>A bounded, immutable Boolean prerequisite tree for one named development pipeline.</summary>
public sealed record CiAiPipelineGateExpression
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumDepth = 8;
    public const int MaximumNodes = 32;

    private CiAiPipelineGateExpression(string pipeline, CiAiGateExpression root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeline);
        ArgumentNullException.ThrowIfNull(root);
        Pipeline = pipeline.Trim();
        if (Pipeline.Length > 100 || Pipeline.Any(char.IsControl))
            throw new ArgumentException("Pipeline names must be at most 100 printable characters.", nameof(pipeline));

        var leaves = new List<CiAiRepositoryGateReference>();
        var nodeCount = Validate(root, 1, leaves);
        if (nodeCount > MaximumNodes)
            throw new ArgumentException($"Gate expressions may contain at most {MaximumNodes} nodes.", nameof(root));
        if (leaves.Select(x => x.GateId).Distinct(StringComparer.Ordinal).Count() != leaves.Count)
            throw new ArgumentException("A gate expression cannot contain duplicate leaf gate identities.", nameof(root));

        Root = root;
        Leaves = Array.AsReadOnly(leaves.ToArray());
        var canonical = $"v{CurrentSchemaVersion}:{Encode(Pipeline)}:{Canonicalize(root)}";
        DefinitionSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public int SchemaVersion => CurrentSchemaVersion;
    public string Pipeline { get; }
    public CiAiGateExpression Root { get; }
    public IReadOnlyList<CiAiRepositoryGateReference> Leaves { get; }
    /// <summary>Canonical SHA-256 identity of this exact versioned expression definition.</summary>
    public string DefinitionSha256 { get; }

    /// <summary>Returns whether a verified leaf-gate event should trigger reevaluation of this pipeline.</summary>
    public bool DependsOnGate(string gateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gateId);
        return Leaves.Any(leaf => string.Equals(leaf.GateId, gateId, StringComparison.Ordinal));
    }

    public static CiAiPipelineGateExpression ForPipeline(string pipeline, CiAiGateExpression root) =>
        new(pipeline, root);

    private static string Encode(string value) => value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + value;

    private static string Canonicalize(CiAiGateExpression expression) => expression switch
    {
        CiAiRepositoryGateReference leaf =>
            leaf.EvidenceKind == CiAiRepositoryGateEvidenceKind.PublishedPackage &&
            leaf.SubjectId is null && leaf.SubjectVersion is null && !leaf.RequiresProtectedPush
                ? "L" + Encode(leaf.GateId) + Encode(leaf.Repository) + Encode(leaf.CandidateId) + Encode(leaf.CommitSha)
                : "D" + ((int)leaf.EvidenceKind).ToString(System.Globalization.CultureInfo.InvariantCulture) +
                  Encode(leaf.GateId) + Encode(leaf.Repository) + Encode(leaf.CandidateId) + Encode(leaf.CommitSha) +
                  Encode(leaf.SubjectId ?? string.Empty) + Encode(leaf.SubjectVersion ?? string.Empty) +
                  (leaf.RequiresProtectedPush ? "P" : "C"),
        CiAiAllOf all =>
            "A" + all.Children.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
            string.Concat(all.Children.Select(Canonicalize)),
        CiAiAnyOf any =>
            "O" + any.Children.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
            string.Concat(any.Children.Select(Canonicalize)),
        _ => throw new ArgumentException("Unsupported gate expression node.", nameof(expression))
    };

    private static int Validate(CiAiGateExpression expression, int depth, ICollection<CiAiRepositoryGateReference> leaves)
    {
        if (depth > MaximumDepth)
            throw new ArgumentException($"Gate expressions may not exceed depth {MaximumDepth}.");

        switch (expression)
        {
            case CiAiRepositoryGateReference leaf:
                leaves.Add(leaf);
                return 1;
            case CiAiAllOf all:
                return 1 + all.Children.Sum(child => Validate(child, depth + 1, leaves));
            case CiAiAnyOf any:
                return 1 + any.Children.Sum(child => Validate(child, depth + 1, leaves));
            default:
                throw new ArgumentException("Unsupported gate expression node.", nameof(expression));
        }
    }
}

/// <summary>Base type for immutable AND/OR prerequisite nodes.</summary>
public abstract record CiAiGateExpression;

/// <summary>A leaf tied to one repository, exact candidate, and one explicit evidence family.</summary>
public sealed record CiAiRepositoryGateReference : CiAiGateExpression
{
    public CiAiRepositoryGateReference(string gateId, string repository, string candidateId, string commitSha)
        : this(gateId, repository, candidateId, commitSha,
            CiAiRepositoryGateEvidenceKind.PublishedPackage, null, null, false)
    {
    }

    public CiAiRepositoryGateReference(
        string gateId,
        string repository,
        string candidateId,
        string commitSha,
        CiAiRepositoryGateEvidenceKind evidenceKind,
        string? subjectId,
        string? subjectVersion,
        bool requiresProtectedPush = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must use canonical owner/repository identity.", nameof(repository));
        if (commitSha is null || commitSha.Length != 40 || !commitSha.All(Uri.IsHexDigit))
            throw new ArgumentException("A full 40-character source commit SHA is required.", nameof(commitSha));
        if (!Enum.IsDefined(evidenceKind))
            throw new ArgumentOutOfRangeException(nameof(evidenceKind));

        var normalizedGateId = gateId.Trim();
        var normalizedCandidateId = candidateId.Trim();
        if (normalizedGateId.Length > 128 || normalizedGateId.Any(char.IsControl))
            throw new ArgumentException("Gate IDs must be at most 128 printable characters.", nameof(gateId));
        if (normalizedCandidateId.Length > 256 || normalizedCandidateId.Any(char.IsControl))
            throw new ArgumentException("Candidate IDs must be at most 256 printable characters.", nameof(candidateId));

        if (evidenceKind == CiAiRepositoryGateEvidenceKind.DevelopmentCapability)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
            ArgumentException.ThrowIfNullOrWhiteSpace(subjectVersion);
        }
        else if (requiresProtectedPush)
        {
            throw new ArgumentException("Protected-push capability trust applies only to development-capability leaves.", nameof(requiresProtectedPush));
        }

        if ((subjectId is null) != (subjectVersion is null))
            throw new ArgumentException("Evidence subject ID and version must be supplied together.");

        GateId = normalizedGateId;
        Repository = repository;
        CandidateId = normalizedCandidateId;
        CommitSha = commitSha.ToLowerInvariant();
        EvidenceKind = evidenceKind;
        SubjectId = subjectId;
        SubjectVersion = subjectVersion;
        RequiresProtectedPush = requiresProtectedPush;
    }

    public string GateId { get; }
    public string Repository { get; }
    public string CandidateId { get; }
    public string CommitSha { get; }
    public CiAiRepositoryGateEvidenceKind EvidenceKind { get; }
    public string? SubjectId { get; }
    public string? SubjectVersion { get; }
    public bool RequiresProtectedPush { get; }
}

/// <summary>Normalized verified evidence for a composite repository-gate leaf.</summary>
public sealed record CiAiRepositoryGateEvaluation
{
    internal CiAiRepositoryGateEvaluation(
        string repository,
        string candidateId,
        string commitSha,
        CiAiRepositoryGateEvidenceKind evidenceKind,
        string? subjectId,
        string? subjectVersion,
        CiAiPipelineGateState state,
        bool isProtectedPushEvidence = false,
        string? errorCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must use canonical owner/repository identity.", nameof(repository));
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        if (commitSha is null || commitSha.Length != 40 || !commitSha.All(Uri.IsHexDigit))
            throw new ArgumentException("A full 40-character source commit SHA is required.", nameof(commitSha));
        if (!Enum.IsDefined(evidenceKind)) throw new ArgumentOutOfRangeException(nameof(evidenceKind));
        if (!Enum.IsDefined(state)) throw new ArgumentOutOfRangeException(nameof(state));
        if (evidenceKind == CiAiRepositoryGateEvidenceKind.DevelopmentCapability &&
            (string.IsNullOrWhiteSpace(subjectId) || string.IsNullOrWhiteSpace(subjectVersion)))
            throw new ArgumentException("Development capability evaluations require an exact capability ID and version.");
        if ((subjectId is null) != (subjectVersion is null))
            throw new ArgumentException("Evidence subject ID and version must be supplied together.");

        Repository = repository;
        CandidateId = candidateId;
        CommitSha = commitSha.ToLowerInvariant();
        EvidenceKind = evidenceKind;
        SubjectId = subjectId;
        SubjectVersion = subjectVersion;
        State = state;
        IsProtectedPushEvidence = isProtectedPushEvidence;
        ErrorCode = errorCode;
    }

    public string Repository { get; }
    public string CandidateId { get; }
    public string CommitSha { get; }
    public CiAiRepositoryGateEvidenceKind EvidenceKind { get; }
    public string? SubjectId { get; }
    public string? SubjectVersion { get; }
    public CiAiPipelineGateState State { get; }
    public bool IsProtectedPushEvidence { get; }
    public string? ErrorCode { get; }

    public static CiAiRepositoryGateEvaluation FromPublishedDependency(DependencyReleaseGateEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        var state = evaluation.CanVerifyConsumer
            ? CiAiPipelineGateState.Satisfied
            : evaluation.State == DependencyReleaseGateState.WaitingForPublishedDependencies
                ? CiAiPipelineGateState.Waiting
                : CiAiPipelineGateState.Blocked;
        return new CiAiRepositoryGateEvaluation(
            evaluation.Repository,
            evaluation.CandidateId,
            evaluation.CommitSha,
            CiAiRepositoryGateEvidenceKind.PublishedPackage,
            evaluation.PackageId,
            evaluation.PackageVersion,
            state,
            false,
            evaluation.Errors.FirstOrDefault());
    }

    public static CiAiRepositoryGateEvaluation FromDevelopmentCapability(
        CiAiDevelopmentCapabilityArtifactBinding binding,
        CiAiDevelopmentCapabilityEvidenceValidation validation)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(validation);
        var evidence = validation.Evidence;
        var protectedPush =
            binding.IsProtectedDevelopmentBranch &&
            binding.SourceRef == "refs/heads/development" &&
            binding.EventName == "push" &&
            binding.EventRef == binding.SourceRef &&
            binding.EventSha == binding.SourceSha &&
            binding.WorkflowRef.EndsWith("@refs/heads/development", StringComparison.Ordinal) &&
            binding.WorkflowSha == binding.SourceSha &&
            binding.CheckoutRef == binding.SourceRef;
        var valid = validation.IsValid &&
            evidence is not null &&
            string.Equals(evidence.ProviderRepository, binding.ProviderRepository, StringComparison.Ordinal) &&
            string.Equals(evidence.CapabilityId, binding.CapabilityId, StringComparison.Ordinal) &&
            string.Equals(evidence.CapabilityVersion, binding.CapabilityVersion, StringComparison.Ordinal) &&
            string.Equals(evidence.SourceRef, binding.SourceRef, StringComparison.Ordinal) &&
            string.Equals(evidence.SourceSha, binding.SourceSha, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(evidence.WorkflowPath, binding.WorkflowPath, StringComparison.Ordinal) &&
            string.Equals(evidence.WorkflowRef, binding.WorkflowRef, StringComparison.Ordinal) &&
            string.Equals(evidence.WorkflowSha, binding.WorkflowSha, StringComparison.OrdinalIgnoreCase) &&
            evidence.WorkflowRunId == binding.WorkflowRunId &&
            evidence.WorkflowAttempt == binding.WorkflowAttempt &&
            string.Equals(evidence.EventName, binding.EventName, StringComparison.Ordinal) &&
            string.Equals(evidence.EventRef, binding.EventRef, StringComparison.Ordinal) &&
            string.Equals(evidence.EventSha, binding.EventSha, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(evidence.CheckoutRef, binding.CheckoutRef, StringComparison.Ordinal) &&
            string.Equals(evidence.CheckoutSha, binding.CheckoutSha, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(evidence.ProviderJobName, binding.ProviderJobName, StringComparison.Ordinal) &&
            evidence.ProviderJobId == binding.ProviderJobId &&
            string.Equals(evidence.ArtifactName, binding.ArtifactName, StringComparison.Ordinal) &&
            evidence.ProviderArtifactId == binding.ProviderArtifactId &&
            string.Equals(evidence.ArtifactDigest, binding.ArtifactDigest, StringComparison.OrdinalIgnoreCase) &&
            evidence.ArtifactSizeBytes == binding.ArtifactSizeBytes &&
            string.Equals(evidence.PolicyId, binding.PolicyId, StringComparison.Ordinal) &&
            evidence.PolicyVersion == binding.PolicyVersion &&
            evidence.BundleId == binding.BundleId &&
            string.Equals(evidence.BundleVersion, binding.BundleVersion, StringComparison.Ordinal) &&
            DictionaryEquals(evidence.DependencyCheckouts, binding.ExpectedDependencyCheckouts) &&
            evidence.TestArtifacts.Count == 2 &&
            evidence.IsProtectedPushEvidence == protectedPush;
        return new CiAiRepositoryGateEvaluation(
            binding.ProviderRepository,
            binding.ProviderRepository + "@" + binding.SourceSha,
            binding.SourceSha,
            CiAiRepositoryGateEvidenceKind.DevelopmentCapability,
            binding.CapabilityId,
            binding.CapabilityVersion,
            valid ? CiAiPipelineGateState.Satisfied : CiAiPipelineGateState.Blocked,
            valid && evidence is not null && evidence.IsProtectedPushEvidence,
            valid ? null : validation.ErrorCode ?? "devcap.validation-binding-mismatch");
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
}

/// <summary>All child gates must be satisfied.</summary>
public sealed record CiAiAllOf : CiAiGateExpression
{
    public CiAiAllOf(IEnumerable<CiAiGateExpression> children) =>
        Children = CopyChildren(children, nameof(children));

    public IReadOnlyList<CiAiGateExpression> Children { get; }

    internal static IReadOnlyList<CiAiGateExpression> CopyChildren(
        IEnumerable<CiAiGateExpression> children, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(children);
        var copy = children.ToArray();
        if (copy.Length < 2)
            throw new ArgumentException("AND/OR expressions require at least two children.", parameterName);
        if (copy.Any(child => child is null))
            throw new ArgumentException("Gate expressions cannot contain null children.", parameterName);
        return Array.AsReadOnly(copy);
    }
}

/// <summary>At least one child gate must be satisfied.</summary>
public sealed record CiAiAnyOf : CiAiGateExpression
{
    public CiAiAnyOf(IEnumerable<CiAiGateExpression> children) =>
        Children = CiAiAllOf.CopyChildren(children, nameof(children));

    public IReadOnlyList<CiAiGateExpression> Children { get; }
}

/// <summary>Evaluates composite gates using only previously verified leaf evaluations.</summary>
public static class CiAiPipelineGateEvaluator
{
    /// <summary>Compatibility overload for published-package evidence.</summary>
    public static CiAiPipelineGateState Evaluate(
        CiAiPipelineGateExpression expression,
        IReadOnlyDictionary<string, DependencyReleaseGateEvaluation> leafEvaluations)
    {
        ArgumentNullException.ThrowIfNull(leafEvaluations);
        var normalized = leafEvaluations.ToDictionary(
            pair => pair.Key,
            pair => CiAiRepositoryGateEvaluation.FromPublishedDependency(pair.Value),
            StringComparer.Ordinal);
        return Evaluate(expression, normalized);
    }

    /// <summary>Evaluates published-package and development-capability evidence through one composite gate engine.</summary>
    public static CiAiPipelineGateState Evaluate(
        CiAiPipelineGateExpression expression,
        IReadOnlyDictionary<string, CiAiRepositoryGateEvaluation> leafEvaluations)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(leafEvaluations);
        return EvaluateNode(expression.Root, leafEvaluations);
    }

    private static CiAiPipelineGateState EvaluateNode(
        CiAiGateExpression node,
        IReadOnlyDictionary<string, CiAiRepositoryGateEvaluation> evaluations)
    {
        switch (node)
        {
            case CiAiRepositoryGateReference leaf:
                if (!evaluations.TryGetValue(leaf.GateId, out var evaluation))
                    return CiAiPipelineGateState.Waiting;
                if (!string.Equals(evaluation.Repository, leaf.Repository, StringComparison.Ordinal) ||
                    !string.Equals(evaluation.CandidateId, leaf.CandidateId, StringComparison.Ordinal) ||
                    !string.Equals(evaluation.CommitSha, leaf.CommitSha, StringComparison.OrdinalIgnoreCase) ||
                    evaluation.EvidenceKind != leaf.EvidenceKind ||
                    (leaf.SubjectId is not null && !string.Equals(evaluation.SubjectId, leaf.SubjectId, StringComparison.Ordinal)) ||
                    (leaf.SubjectVersion is not null && !string.Equals(evaluation.SubjectVersion, leaf.SubjectVersion, StringComparison.Ordinal)))
                    return CiAiPipelineGateState.Blocked;
                if (evaluation.State == CiAiPipelineGateState.Satisfied &&
                    leaf.RequiresProtectedPush && !evaluation.IsProtectedPushEvidence)
                    return CiAiPipelineGateState.Waiting;
                return evaluation.State;

            case CiAiAllOf all:
            {
                var states = all.Children.Select(child => EvaluateNode(child, evaluations)).ToArray();
                if (states.Contains(CiAiPipelineGateState.Blocked)) return CiAiPipelineGateState.Blocked;
                return states.All(state => state == CiAiPipelineGateState.Satisfied)
                    ? CiAiPipelineGateState.Satisfied
                    : CiAiPipelineGateState.Waiting;
            }

            case CiAiAnyOf any:
            {
                var states = any.Children.Select(child => EvaluateNode(child, evaluations)).ToArray();
                if (states.Contains(CiAiPipelineGateState.Satisfied)) return CiAiPipelineGateState.Satisfied;
                return states.All(state => state == CiAiPipelineGateState.Blocked)
                    ? CiAiPipelineGateState.Blocked
                    : CiAiPipelineGateState.Waiting;
            }

            default:
                return CiAiPipelineGateState.Blocked;
        }
    }
}


/// <summary>LLM allocation disposition derived from a composite prerequisite result.</summary>
public enum CiAiPipelineGateAdmissionState
{
    DormantWaiting = 0,
    DeniedBlocked = 1,
    Eligible = 2
}

/// <summary>A deterministic admission decision; it does not itself dispatch or schedule an LLM call.</summary>
public sealed record CiAiPipelineGateAdmission(CiAiPipelineGateAdmissionState State, string Reason)
{
    public bool MayAllocateLlm => State == CiAiPipelineGateAdmissionState.Eligible;
}

/// <summary>Prevents a repository with unresolved prerequisites from consuming LLM allocation.</summary>
public static class CiAiPipelineGateAdmissionPolicy
{
    public static CiAiPipelineGateAdmission Evaluate(CiAiPipelineGateState gateState) => gateState switch
    {
        CiAiPipelineGateState.Satisfied =>
            new(CiAiPipelineGateAdmissionState.Eligible, "pipeline.gates-satisfied"),
        CiAiPipelineGateState.Waiting =>
            new(CiAiPipelineGateAdmissionState.DormantWaiting, "pipeline.gates-waiting"),
        CiAiPipelineGateState.Blocked =>
            new(CiAiPipelineGateAdmissionState.DeniedBlocked, "pipeline.gates-blocked"),
        _ => new(CiAiPipelineGateAdmissionState.DeniedBlocked, "pipeline.gates-state-unknown")
    };
}
