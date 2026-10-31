using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Machine-readable proof emitted by the trusted workflow after it resolves and checks out one candidate SHA.
/// This is a provenance fact, not a success verdict or authorization.
/// </summary>
public sealed record CiWorkflowCandidateCheckoutProvenance
{
    public const int CurrentContractVersion = 1;
    public const string CurrentSchema = "ciai.workflow-candidate-checkout.v1";

    public CiWorkflowCandidateCheckoutProvenance(
        string repository,
        string workflowPath,
        long runId,
        int runAttempt,
        string requestId,
        string correlationId,
        string taskId,
        int attemptNumber,
        string workflowDefinitionRef,
        string workflowDefinitionSha,
        string candidateRef,
        string candidateSha,
        string? checkedOutSha,
        bool checkoutVerified,
        DateTimeOffset observedAt)
    {
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must be canonical owner/repository identity.", nameof(repository));
        if (!CiWorkflowRerunRequest.IsWorkflowPath(workflowPath))
            throw new ArgumentException("Workflow path must identify a root-level workflow file.", nameof(workflowPath));
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        if (runAttempt <= 0) throw new ArgumentOutOfRangeException(nameof(runAttempt));
        if (!CiWorkflowDispatchInputContract.IsWorkflowIdentity(requestId) ||
            !CiWorkflowDispatchInputContract.IsWorkflowIdentity(correlationId) ||
            !CiWorkflowDispatchInputContract.IsWorkflowIdentity(taskId))
            throw new ArgumentException("Request, correlation, and task IDs must use the bounded workflow identity format.");
        if (attemptNumber is < 1 or > CiWorkflowRerunRequest.MaximumAllowedAttempts)
            throw new ArgumentOutOfRangeException(nameof(attemptNumber));
        if (!string.Equals(workflowDefinitionRef, CiWorkflowDispatchInputContract.TrustedWorkflowRef, StringComparison.Ordinal))
            throw new ArgumentException("Workflow definition must come from the trusted workflow ref.", nameof(workflowDefinitionRef));
        if (!IsSha(workflowDefinitionSha))
            throw new ArgumentException("Workflow definition SHA must be a full 40-character hexadecimal identifier.", nameof(workflowDefinitionSha));
        if (!CiWorkflowRerunRequest.IsFeatureBranchRef(candidateRef))
            throw new ArgumentException("Candidate ref must be an isolated feature branch.", nameof(candidateRef));
        if (!IsSha(candidateSha))
            throw new ArgumentException("Candidate SHA must be a full 40-character hexadecimal identifier.", nameof(candidateSha));
        if (checkedOutSha is not null && !IsSha(checkedOutSha))
            throw new ArgumentException("Checked-out SHA must be null or a full 40-character hexadecimal identifier.", nameof(checkedOutSha));
        if (checkoutVerified && !string.Equals(checkedOutSha, candidateSha, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A verified checkout must equal the requested candidate SHA.", nameof(checkedOutSha));
        if (observedAt == default)
            throw new ArgumentException("Observation timestamp is required.", nameof(observedAt));

        ContractVersion = CurrentContractVersion;
        Schema = CurrentSchema;
        Repository = repository;
        WorkflowPath = workflowPath;
        RunId = runId;
        RunAttempt = runAttempt;
        RequestId = requestId;
        CorrelationId = correlationId;
        TaskId = taskId;
        AttemptNumber = attemptNumber;
        WorkflowDefinitionRef = workflowDefinitionRef;
        WorkflowDefinitionSha = workflowDefinitionSha.ToLowerInvariant();
        CandidateRef = candidateRef;
        CandidateSha = candidateSha.ToLowerInvariant();
        CheckedOutSha = checkedOutSha?.ToLowerInvariant();
        CheckoutVerified = checkoutVerified;
        ObservedAt = observedAt;
    }

    public int ContractVersion { get; }
    public string Schema { get; }
    public string Repository { get; }
    public string WorkflowPath { get; }
    public long RunId { get; }
    public int RunAttempt { get; }
    public string RequestId { get; }
    public string CorrelationId { get; }
    public string TaskId { get; }
    public int AttemptNumber { get; }
    public string WorkflowDefinitionRef { get; }
    public string WorkflowDefinitionSha { get; }
    public string CandidateRef { get; }
    public string CandidateSha { get; }
    public string? CheckedOutSha { get; }
    public bool CheckoutVerified { get; }
    public DateTimeOffset ObservedAt { get; }

    public string ToJson(bool indented = true) => JsonSerializer.Serialize(this, JsonOptions(indented));

    public static CiWorkflowCandidateCheckoutProvenance FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("contractVersion", out var version) ||
            !version.TryGetInt32(out var parsedVersion) ||
            parsedVersion != CurrentContractVersion ||
            !root.TryGetProperty("schema", out var schema) ||
            schema.ValueKind != JsonValueKind.String ||
            !string.Equals(schema.GetString(), CurrentSchema, StringComparison.Ordinal))
            throw new InvalidDataException("Candidate checkout provenance contract version or schema is unsupported.");

        return JsonSerializer.Deserialize<CiWorkflowCandidateCheckoutProvenance>(json, JsonOptions(false))
            ?? throw new InvalidDataException("Candidate checkout provenance JSON produced no record.");
    }

    private static bool IsSha(string value) =>
        value.Length == 40 && value.All(Uri.IsHexDigit);

    private static JsonSerializerOptions JsonOptions(bool indented) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = indented
    };
}
