using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Evidence packet v2 explicitly separates the trusted workflow definition revision from the candidate
/// revision checked out and tested by that workflow. The nested v1 packet remains the exact GitHub run record.
/// </summary>
public sealed record CiWorkflowEvidencePacketV2
{
    public const int CurrentContractVersion = 2;
    public const string CurrentSchema = "ciai.workflow-evidence.v2";

    public CiWorkflowEvidencePacketV2(
        CiWorkflowEvidencePacket workflowExecution,
        CiWorkflowCandidateCheckoutProvenance candidateCheckout)
    {
        ArgumentNullException.ThrowIfNull(workflowExecution);
        ArgumentNullException.ThrowIfNull(candidateCheckout);

        if (!string.Equals(workflowExecution.Event, "workflow_dispatch", StringComparison.Ordinal))
            throw new ArgumentException("Candidate checkout evidence v2 requires a workflow_dispatch run.", nameof(workflowExecution));
        if (!string.Equals(workflowExecution.Repository, candidateCheckout.Repository, StringComparison.Ordinal) ||
            !string.Equals(workflowExecution.WorkflowPath, candidateCheckout.WorkflowPath, StringComparison.Ordinal) ||
            workflowExecution.RunId != candidateCheckout.RunId ||
            workflowExecution.RunAttempt != candidateCheckout.RunAttempt)
            throw new ArgumentException("Candidate checkout provenance must identify the exact same repository, workflow, run, and attempt.", nameof(candidateCheckout));
        if (!string.Equals(workflowExecution.HeadRef, candidateCheckout.WorkflowDefinitionRef, StringComparison.Ordinal) ||
            !string.Equals(workflowExecution.HeadSha, candidateCheckout.WorkflowDefinitionSha, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Workflow-definition provenance must match the GitHub run-level ref and SHA.", nameof(candidateCheckout));

        ContractVersion = CurrentContractVersion;
        Schema = CurrentSchema;
        WorkflowExecution = workflowExecution;
        CandidateCheckout = candidateCheckout;
        WorkflowRef = candidateCheckout.WorkflowDefinitionRef;
        WorkflowSha = candidateCheckout.WorkflowDefinitionSha;
        CandidateRef = candidateCheckout.CandidateRef;
        CandidateSha = candidateCheckout.CandidateSha;
        CheckedOutSha = candidateCheckout.CheckedOutSha;
        CandidateCheckoutVerified = candidateCheckout.CheckoutVerified;
        RequestId = candidateCheckout.RequestId;
        CorrelationId = candidateCheckout.CorrelationId;
        TaskId = candidateCheckout.TaskId;
        AttemptNumber = candidateCheckout.AttemptNumber;
    }

    public int ContractVersion { get; }
    public string Schema { get; }
    public string WorkflowRef { get; }
    public string WorkflowSha { get; }
    public string CandidateRef { get; }
    public string CandidateSha { get; }
    public string? CheckedOutSha { get; }
    public bool CandidateCheckoutVerified { get; }
    public string RequestId { get; }
    public string CorrelationId { get; }
    public string TaskId { get; }
    public int AttemptNumber { get; }
    public CiWorkflowEvidencePacket WorkflowExecution { get; }
    public CiWorkflowCandidateCheckoutProvenance CandidateCheckout { get; }

    public string Repository => WorkflowExecution.Repository;
    public string WorkflowPath => WorkflowExecution.WorkflowPath;
    public long RunId => WorkflowExecution.RunId;
    public int RunAttempt => WorkflowExecution.RunAttempt;
    public bool EvidenceComplete => WorkflowExecution.EvidenceComplete;

    public bool IsSuccessful =>
        EvidenceComplete &&
        CandidateCheckoutVerified &&
        string.Equals(CheckedOutSha, CandidateSha, StringComparison.OrdinalIgnoreCase) &&
        WorkflowExecution.IsSuccessful;

    public IReadOnlyList<string> BlockingReasons
    {
        get
        {
            var reasons = WorkflowExecution.BlockingReasons.ToList();
            if (!CandidateCheckoutVerified) reasons.Add("candidate.checkout-not-verified");
            if (!string.Equals(CheckedOutSha, CandidateSha, StringComparison.OrdinalIgnoreCase))
                reasons.Add("candidate.checkout-sha-mismatch");
            return Array.AsReadOnly(reasons.Distinct(StringComparer.Ordinal).ToArray());
        }
    }

    public string ToJson(bool indented = true) => JsonSerializer.Serialize(this, JsonOptions(indented));

    public static CiWorkflowEvidencePacketV2 FromJson(string json)
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
            throw new InvalidDataException("Evidence packet v2 contract version or schema is unsupported.");

        var packet = JsonSerializer.Deserialize<CiWorkflowEvidencePacketV2>(json, JsonOptions(false))
            ?? throw new InvalidDataException("Evidence packet v2 JSON produced no packet.");
        if (!string.Equals(packet.WorkflowRef, packet.WorkflowExecution.HeadRef, StringComparison.Ordinal) ||
            !string.Equals(packet.WorkflowSha, packet.WorkflowExecution.HeadSha, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(packet.CandidateRef, packet.CandidateCheckout.CandidateRef, StringComparison.Ordinal) ||
            !string.Equals(packet.CandidateSha, packet.CandidateCheckout.CandidateSha, StringComparison.OrdinalIgnoreCase) ||
            packet.CandidateCheckoutVerified != packet.CandidateCheckout.CheckoutVerified)
            throw new InvalidDataException("Evidence packet v2 flattened provenance does not match its nested records.");
        return packet;
    }

    private static JsonSerializerOptions JsonOptions(bool indented) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = indented,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
