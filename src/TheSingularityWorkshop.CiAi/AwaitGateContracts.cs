using System.Security.Cryptography;
using System.Text;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Approval source recorded by a trusted host before a gate contract becomes binding.</summary>
public enum AwaitGateApprovalKind
{
    Human = 0,
    TrustedPolicyAuthority = 1
}

/// <summary>
/// Explicit approval metadata for a proposed gate contract. A host must authenticate the approver,
/// validate the authority policy, and verify the approval reference before constructing a binding contract.
/// </summary>
public sealed record AwaitGateApproval
{
    public AwaitGateApproval(string approvalReference, string authorityPolicyId,
        string approverIdentity, AwaitGateApprovalKind kind, DateTimeOffset approvedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorityPolicyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(approverIdentity);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        ApprovalReference = approvalReference;
        AuthorityPolicyId = authorityPolicyId;
        ApproverIdentity = approverIdentity;
        Kind = kind;
        ApprovedAt = approvedAt;
    }

    public string ApprovalReference { get; }
    public string AuthorityPolicyId { get; }
    public string ApproverIdentity { get; }
    public AwaitGateApprovalKind Kind { get; }
    public DateTimeOffset ApprovedAt { get; }
}

/// <summary>Evidence mode permitted by an approved gate requirement.</summary>
public enum AwaitGateEvidenceKind
{
    Machine = 0,
    HumanAdjudication = 1
}

/// <summary>Overall result of evaluating one immutable gate contract against attributed evidence.</summary>
public enum AwaitGateState
{
    Pending = 0,
    Satisfied = 1,
    Rejected = 2,
    Indeterminate = 3,
    Revoked = 4
}

/// <summary>Stable identity of a gate requirement and its exact expected value.</summary>
public sealed record AwaitGateRequirement
{
    public AwaitGateRequirement(string requirementId, string description, string expectedValue,
        AwaitGateEvidenceKind evidenceKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requirementId);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedValue);
        if (!Enum.IsDefined(evidenceKind))
            throw new ArgumentOutOfRangeException(nameof(evidenceKind));
        if (requirementId != requirementId.Trim() || expectedValue != expectedValue.Trim())
            throw new ArgumentException("Requirement identity and expected value cannot have surrounding whitespace.");
        RequirementId = requirementId;
        Description = description.Trim();
        ExpectedValue = expectedValue;
        EvidenceKind = evidenceKind;
    }

    public string RequirementId { get; }
    public string Description { get; }
    public string ExpectedValue { get; }
    public AwaitGateEvidenceKind EvidenceKind { get; }
}

/// <summary>
/// Approved, versioned acceptance contract for a provider capability. This contract is descriptive;
/// it does not authenticate a provider or authorize a host to resume work.
/// </summary>
public sealed record AwaitGateContract
{
    public AwaitGateContract(string gateId, int version, string consumerRepository,
        string providerRepository, string providerSourceSha, string manifestDigest,
        string evaluationPolicyVersion, AwaitGateApproval approval,
        IReadOnlyList<AwaitGateRequirement> requirements)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gateId);
        if (gateId != gateId.Trim())
            throw new ArgumentException("Gate ID cannot have surrounding whitespace.", nameof(gateId));
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version));
        ValidateRepository(consumerRepository, nameof(consumerRepository));
        ValidateRepository(providerRepository, nameof(providerRepository));
        ValidateSha(providerSourceSha, nameof(providerSourceSha));
        ValidateDigest(manifestDigest, nameof(manifestDigest));
        ArgumentException.ThrowIfNullOrWhiteSpace(evaluationPolicyVersion);
        ArgumentNullException.ThrowIfNull(approval);
        if (evaluationPolicyVersion != evaluationPolicyVersion.Trim())
            throw new ArgumentException("Policy version cannot have surrounding whitespace.", nameof(evaluationPolicyVersion));
        ArgumentNullException.ThrowIfNull(requirements);
        var copy = requirements.ToArray();
        if (copy.Length == 0 || copy.Any(item => item is null))
            throw new ArgumentException("A gate requires at least one non-null requirement.", nameof(requirements));
        if (copy.Select(item => item.RequirementId).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Requirement IDs must be unique within a gate.", nameof(requirements));

        GateId = gateId;
        Version = version;
        ConsumerRepository = consumerRepository;
        ProviderRepository = providerRepository;
        ProviderSourceSha = providerSourceSha.ToLowerInvariant();
        ManifestDigest = manifestDigest;
        ContractDigest = contractDigest;
        EvaluationPolicyVersion = evaluationPolicyVersion;
        Approval = approval;
        Requirements = Array.AsReadOnly(copy);
        ContractDigest = ComputeContractDigest();
    }

    public string GateId { get; }
    public int Version { get; }
    public string ConsumerRepository { get; }
    public string ProviderRepository { get; }
    public string ProviderSourceSha { get; }
    public string ManifestDigest { get; }
    public string ContractDigest { get; }
    public string EvaluationPolicyVersion { get; }
    public AwaitGateApproval Approval { get; }
    public IReadOnlyList<AwaitGateRequirement> Requirements { get; }

    private string ComputeContractDigest()
    {
        var fields = new List<string>
        {
            "ciai.await-gate-contract.v1", GateId,
            Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ConsumerRepository, ProviderRepository, ProviderSourceSha,
            ManifestDigest.ToLowerInvariant(), EvaluationPolicyVersion,
            Approval.ApprovalReference, Approval.AuthorityPolicyId, Approval.ApproverIdentity,
            Approval.Kind.ToString(),
            Approval.ApprovedAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)
        };
        foreach (var requirement in Requirements.OrderBy(item => item.RequirementId, StringComparer.Ordinal))
        {
            fields.Add(requirement.RequirementId);
            fields.Add(requirement.Description);
            fields.Add(requirement.ExpectedValue);
            fields.Add(requirement.EvidenceKind.ToString());
        }
        var canonical = string.Concat(fields.Select(value =>
            value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + value));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static void ValidateRepository(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!RepositoryIdentity.IsValid(value))
            throw new ArgumentException("Repository must be canonical owner/repository.", parameter);
    }

    internal static void ValidateSha(string value, string parameter)
    {
        if (value is null || value.Length != 40 || !value.All(Uri.IsHexDigit))
            throw new ArgumentException("A full 40-character source SHA is required.", parameter);
    }

    internal static void ValidateDigest(string value, string parameter)
    {
        if (value is null || value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new ArgumentException("A full 64-character SHA-256 digest is required.", parameter);
    }
}

/// <summary>
/// Evidence attributed to a provider artifact or an authorized human decision. Authentication and
/// protected-policy checks are host responsibilities; callers must supply evidence only after those checks.
/// </summary>
public sealed record AwaitGateEvidence
{
    public AwaitGateEvidence(string requirementId, string gateId, int gateVersion,
        string contractDigest, string providerRepository, string providerSourceSha,
        string manifestDigest, string evidenceDigest, AwaitGateEvidenceKind evidenceKind,
        string value, string authorityReference, string? workflowRunId = null,
        int workflowAttempt = 0, string? authorityPolicyId = null, string? adjudicatorId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requirementId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gateId);
        if (gateVersion < 1) throw new ArgumentOutOfRangeException(nameof(gateVersion));
        AwaitGateContract.ValidateDigest(contractDigest, nameof(contractDigest));
        ArgumentException.ThrowIfNullOrWhiteSpace(providerRepository);
        if (!RepositoryIdentity.IsValid(providerRepository))
            throw new ArgumentException("Repository must be canonical owner/repository.", nameof(providerRepository));
        AwaitGateContract.ValidateSha(providerSourceSha, nameof(providerSourceSha));
        AwaitGateContract.ValidateDigest(manifestDigest, nameof(manifestDigest));
        AwaitGateContract.ValidateDigest(evidenceDigest, nameof(evidenceDigest));
        if (!Enum.IsDefined(evidenceKind)) throw new ArgumentOutOfRangeException(nameof(evidenceKind));
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorityReference);

        if (evidenceKind == AwaitGateEvidenceKind.Machine &&
            (string.IsNullOrWhiteSpace(workflowRunId) || workflowAttempt < 1))
            throw new ArgumentException("Machine evidence requires an exact workflow run ID and positive attempt.");
        if (evidenceKind == AwaitGateEvidenceKind.HumanAdjudication &&
            (string.IsNullOrWhiteSpace(authorityPolicyId) || string.IsNullOrWhiteSpace(adjudicatorId)))
            throw new ArgumentException("Human adjudication requires an authority policy and adjudicator identity.");

        RequirementId = requirementId;
        GateId = gateId;
        GateVersion = gateVersion;
        ContractDigest = contractDigest;
        ProviderRepository = providerRepository;
        ProviderSourceSha = providerSourceSha.ToLowerInvariant();
        ManifestDigest = manifestDigest;
        EvidenceDigest = evidenceDigest;
        EvidenceKind = evidenceKind;
        Value = value;
        AuthorityReference = authorityReference;
        WorkflowRunId = workflowRunId;
        WorkflowAttempt = workflowAttempt;
        AuthorityPolicyId = authorityPolicyId;
        AdjudicatorId = adjudicatorId;
    }

    public string RequirementId { get; }
    public string GateId { get; }
    public int GateVersion { get; }
    public string ContractDigest { get; }
    public string ProviderRepository { get; }
    public string ProviderSourceSha { get; }
    public string ManifestDigest { get; }
    public string EvidenceDigest { get; }
    public AwaitGateEvidenceKind EvidenceKind { get; }
    public string Value { get; }
    public string AuthorityReference { get; }
    public string? WorkflowRunId { get; }
    public int WorkflowAttempt { get; }
    public string? AuthorityPolicyId { get; }
    public string? AdjudicatorId { get; }
}

/// <summary>Deterministic status for one gate requirement.</summary>
public sealed record AwaitGateRequirementResult(string RequirementId, AwaitGateState State, string ReasonCode);

/// <summary>Deterministic evaluation bound to one gate version, source, manifest, and policy.</summary>
public sealed record AwaitGateEvaluation
{
    internal AwaitGateEvaluation(string gateId, int gateVersion, string contractDigest,
        string providerRepository, string providerSourceSha, string manifestDigest,
        string evaluationPolicyVersion, AwaitGateState state,
        IReadOnlyList<AwaitGateRequirementResult> requirements)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gateId);
        if (gateVersion < 1) throw new ArgumentOutOfRangeException(nameof(gateVersion));
        ArgumentNullException.ThrowIfNull(requirements);
        if (!Enum.IsDefined(state)) throw new ArgumentOutOfRangeException(nameof(state));
        GateId = gateId;
        GateVersion = gateVersion;
        ContractDigest = contractDigest;
        ProviderRepository = providerRepository;
        ProviderSourceSha = providerSourceSha;
        ManifestDigest = manifestDigest;
        EvaluationPolicyVersion = evaluationPolicyVersion;
        State = state;
        Requirements = Array.AsReadOnly(requirements.ToArray());
    }

    public string GateId { get; }
    public int GateVersion { get; }
    public string ContractDigest { get; }
    public string ProviderRepository { get; }
    public string ProviderSourceSha { get; }
    public string ManifestDigest { get; }
    public string EvaluationPolicyVersion { get; }
    public AwaitGateState State { get; }
    public IReadOnlyList<AwaitGateRequirementResult> Requirements { get; }
    public bool IsSatisfied => State == AwaitGateState.Satisfied;
}

/// <summary>
/// Evaluates exact, pre-authenticated evidence without executing work. Missing evidence stays pending;
/// contradictory or mismatched evidence stays indeterminate; negative evidence rejects the gate.
/// </summary>
public static class AwaitGateEvaluator
{
    public static AwaitGateEvaluation Evaluate(AwaitGateContract contract,
        IEnumerable<AwaitGateEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(evidence);
        var supplied = evidence.ToArray();
        if (supplied.Any(item => item is null))
            throw new ArgumentException("Evidence cannot contain null entries.", nameof(evidence));
        var results = new List<AwaitGateRequirementResult>();
        foreach (var requirement in contract.Requirements)
        {
            var matches = supplied.Where(item =>
                string.Equals(item.RequirementId, requirement.RequirementId, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 0)
            {
                results.Add(new(requirement.RequirementId, AwaitGateState.Pending, "evidence.missing"));
                continue;
            }
            if (matches.Length != 1)
            {
                results.Add(new(requirement.RequirementId, AwaitGateState.Indeterminate, "evidence.duplicate"));
                continue;
            }

            var item = matches[0];
            if (!string.Equals(item.GateId, contract.GateId, StringComparison.Ordinal) ||
                item.GateVersion != contract.Version ||
                !string.Equals(item.ContractDigest, contract.ContractDigest, StringComparison.Ordinal) ||
                !string.Equals(item.ProviderRepository, contract.ProviderRepository, StringComparison.Ordinal) ||
                !string.Equals(item.ProviderSourceSha, contract.ProviderSourceSha, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(item.ManifestDigest, contract.ManifestDigest, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new(requirement.RequirementId, AwaitGateState.Indeterminate, "evidence.provenance-mismatch"));
                continue;
            }
            if (item.EvidenceKind != requirement.EvidenceKind)
            {
                results.Add(new(requirement.RequirementId, AwaitGateState.Indeterminate, "evidence.authority-kind-mismatch"));
                continue;
            }
            results.Add(string.Equals(item.Value, requirement.ExpectedValue, StringComparison.Ordinal)
                ? new(requirement.RequirementId, AwaitGateState.Satisfied, "requirement.satisfied")
                : new(requirement.RequirementId, AwaitGateState.Rejected, "requirement.value-mismatch"));
        }

        var state = results.Any(result => result.State == AwaitGateState.Rejected)
            ? AwaitGateState.Rejected
            : results.Any(result => result.State == AwaitGateState.Indeterminate)
                ? AwaitGateState.Indeterminate
                : results.Any(result => result.State == AwaitGateState.Pending)
                    ? AwaitGateState.Pending
                    : AwaitGateState.Satisfied;

        return new(contract.GateId, contract.Version, contract.ContractDigest,
            contract.ProviderRepository, contract.ProviderSourceSha, contract.ManifestDigest,
            contract.EvaluationPolicyVersion, state, Array.AsReadOnly(results.ToArray()));
    }
}

/// <summary>One durable dependency declaration for a consumer workstream.</summary>
public sealed record AwaitRegistration
{
    public AwaitRegistration(string awaitId, string consumerRepository, string workstreamId,
        IReadOnlyList<AwaitGateReference> requiredGates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(awaitId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workstreamId);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerRepository);
        if (!RepositoryIdentity.IsValid(consumerRepository))
            throw new ArgumentException("Repository must be canonical owner/repository.", nameof(consumerRepository));
        ArgumentNullException.ThrowIfNull(requiredGates);
        var copy = requiredGates.ToArray();
        if (copy.Length == 0 || copy.Any(item => item is null))
            throw new ArgumentException("An Await must declare at least one gate.", nameof(requiredGates));
        if (copy.Select(item => item.Identity).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("An Await cannot repeat a gate identity.", nameof(requiredGates));
        AwaitId = awaitId;
        ConsumerRepository = consumerRepository;
        WorkstreamId = workstreamId;
        RequiredGates = Array.AsReadOnly(copy);
    }

    public string AwaitId { get; }
    public string ConsumerRepository { get; }
    public string WorkstreamId { get; }
    public IReadOnlyList<AwaitGateReference> RequiredGates { get; }
    public bool IsReady(IEnumerable<AwaitGateEvaluation> evaluations)
    {
        ArgumentNullException.ThrowIfNull(evaluations);
        var items = evaluations.ToArray();
        return RequiredGates.All(gate => items.Count(item =>
            string.Equals(item.GateId, gate.GateId, StringComparison.Ordinal) &&
            item.GateVersion == gate.Version &&
            string.Equals(item.ProviderRepository, gate.ProviderRepository, StringComparison.Ordinal) &&
            string.Equals(item.ContractDigest, gate.ContractDigest, StringComparison.Ordinal) &&
            item.State == AwaitGateState.Satisfied) == 1);
    }
}

/// <summary>Versioned reference to a gate required by one Await registration.</summary>
public sealed record AwaitGateReference
{
    public AwaitGateReference(string gateId, int version, string contractDigest, string providerRepository)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gateId);
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        AwaitGateContract.ValidateDigest(contractDigest, nameof(contractDigest));
        if (!RepositoryIdentity.IsValid(providerRepository))
            throw new ArgumentException("Repository must be canonical owner/repository.", nameof(providerRepository));
        GateId = gateId;
        Version = version;
        ContractDigest = contractDigest;
        ProviderRepository = providerRepository;
    }

    public string GateId { get; }
    public int Version { get; }
    public string ContractDigest { get; }
    public string ProviderRepository { get; }
    public string Identity => $"{ProviderRepository}:{GateId}:v{Version}:{ContractDigest}";
}
