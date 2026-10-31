using System.Security.Cryptography;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Version 3 evidence envelope. Unlike v2, this contract requires a separately parsed,
/// repository/run/attempt-bound workflow provenance artifact. The legacy packet is retained
/// only as the job/run outcome observation; its v2 workflow/candidate SHA aliases are not
/// authoritative in this envelope.
/// </summary>
public sealed record CiWorkflowEvidencePacketV3
{
    public const int CurrentContractVersion = 3;
    public const string CurrentSchema = "ciai.workflow-evidence.v3";

    private CiWorkflowEvidencePacketV3(
        CiWorkflowEvidencePacket observedRun,
        CiWorkflowProvenanceRecord provenance,
        bool workflowDefinitionTrusted,
        bool checkoutVerified)
    {
        ObservedRun = observedRun;
        Provenance = provenance;
        WorkflowDefinitionTrusted = workflowDefinitionTrusted;
        CheckoutVerified = checkoutVerified;
        CandidateRef = provenance.CheckoutRef;
        CandidateSha = provenance.CheckoutSha;
        BlockingReasons = Array.AsReadOnly(BuildBlockingReasons().ToArray());
    }

    /// <summary>
    /// Creates v3 only from exact-run provenance already parsed by the trusted artifact retriever.
    /// The host must set workflowDefinitionTrusted only after independently matching WorkflowRef
    /// and WorkflowSha against its protected control-plane policy. This flag is not a substitute
    /// for that host-side verification.
    /// </summary>
    /// <summary>
    /// Builds v3 from the exact collected run/job attempt and a separately validated provenance
    /// artifact. The caller must obtain provenance through the trusted provider artifact boundary
    /// and apply protected workflow-definition policy before setting the trust argument.
    /// </summary>
    public static CiWorkflowEvidencePacketV3 FromCollections(
        CiWorkflowRunCollection runCollection,
        CiWorkflowJobCollection jobCollection,
        long runId,
        int runAttempt,
        DateTimeOffset observedAt,
        CiWorkflowProvenanceRecord provenance,
        bool workflowDefinitionTrusted,
        CiWorkflowCandidateCheckoutEvidence? candidateCheckoutEvidence = null,
        IReadOnlyList<Uri>? artifactReferences = null)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        var observedRun = CiWorkflowEvidencePacket.FromCollections(
            runCollection,
            jobCollection,
            runId,
            runAttempt,
            observedAt,
            candidateCheckoutEvidence,
            artifactReferences);
        return FromValidatedProvenance(
            observedRun,
            provenance,
            workflowDefinitionTrusted,
            candidateCheckoutEvidence);
    }

    public static CiWorkflowEvidencePacketV3 FromValidatedProvenance(
        CiWorkflowEvidencePacket observedRun,
        CiWorkflowProvenanceRecord provenance,
        bool workflowDefinitionTrusted,
        CiWorkflowCandidateCheckoutEvidence? candidateCheckoutEvidence = null)
    {
        ArgumentNullException.ThrowIfNull(observedRun);
        ArgumentNullException.ThrowIfNull(provenance);

        if (!string.Equals(observedRun.Repository, provenance.Repository, StringComparison.OrdinalIgnoreCase) ||
            observedRun.RunId != provenance.RunId ||
            observedRun.RunAttempt != provenance.RunAttempt)
            throw new InvalidDataException("Workflow provenance does not identify the exact observed repository, run, and attempt.");

        if (!string.Equals(observedRun.WorkflowPath, provenance.WorkflowPath, StringComparison.Ordinal))
            throw new InvalidDataException("Workflow provenance path does not match the observed workflow path.");

        if (!string.Equals(observedRun.Event, provenance.EventName, StringComparison.Ordinal))
            throw new InvalidDataException("Workflow provenance event name does not match the observed run event.");

        bool checkoutVerified;
        if (string.Equals(provenance.EventName, "workflow_dispatch", StringComparison.Ordinal) &&
            candidateCheckoutEvidence is not null)
        {
            if (!string.Equals(candidateCheckoutEvidence.CandidateRef, provenance.CheckoutRef, StringComparison.Ordinal) ||
                !string.Equals(candidateCheckoutEvidence.CandidateSha, provenance.CheckoutSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Candidate checkout evidence does not match the independently recorded checkout ref and SHA.");

            // The v2 job observation is used only to corroborate the named resolution and
            // verification steps. The actual ref/SHA comes exclusively from the v1 provenance artifact.
            checkoutVerified = observedRun.CandidateCheckoutVerified;
        }
        else
        {
            if (candidateCheckoutEvidence is not null)
                throw new InvalidDataException("Separate candidate checkout evidence is valid only for workflow_dispatch.");

            // For ordinary push/PR runs the actual checkout must equal the event commit.
            // PR event SHA is normally the merge commit, not the PR source head SHA.
            checkoutVerified =
                string.Equals(provenance.CheckoutRef, provenance.EventRef, StringComparison.Ordinal) &&
                string.Equals(provenance.CheckoutSha, provenance.EventSha, StringComparison.OrdinalIgnoreCase);
        }

        return new CiWorkflowEvidencePacketV3(
            observedRun, provenance, workflowDefinitionTrusted, checkoutVerified);
    }

    public int ContractVersion => CurrentContractVersion;
    public string Schema => CurrentSchema;
    public CiWorkflowEvidencePacket ObservedRun { get; }
    public CiWorkflowProvenanceRecord Provenance { get; }
    public bool WorkflowDefinitionTrusted { get; }
    public string CandidateRef { get; }
    public string CandidateSha { get; }
    public bool CheckoutVerified { get; }
    public bool EvidenceComplete => ObservedRun.EvidenceComplete;
    public bool IsSuccessful =>
        WorkflowDefinitionTrusted &&
        CheckoutVerified &&
        EvidenceComplete &&
        ObservedRun.Conclusion == CiRunConclusion.Success &&
        ObservedRun.Jobs.All(job => job.Status == "completed" && job.Conclusion == "success");

    public IReadOnlyList<string> BlockingReasons { get; }

    /// <summary>
    /// Serializes the v3 envelope without persisting the host's workflow-trust decision.
    /// Trust must be re-evaluated by the host whenever the durable packet is reloaded.
    /// </summary>
    public string ToJson(bool indented = true)
    {
        var pullRequest = Provenance.PullRequest is null ? null : new
        {
            number = Provenance.PullRequest.Number,
            head = new { @ref = Provenance.PullRequest.HeadRef, sha = Provenance.PullRequest.HeadSha },
            @base = new { @ref = Provenance.PullRequest.BaseRef, sha = Provenance.PullRequest.BaseSha }
        };
        var provenance = new
        {
            schema = CiWorkflowProvenanceRecord.CurrentSchema,
            repository = Provenance.Repository,
            runId = Provenance.RunId,
            runAttempt = Provenance.RunAttempt,
            workflow = new { @ref = Provenance.WorkflowRef, sha = Provenance.WorkflowSha },
            @event = new
            {
                name = Provenance.EventName,
                @ref = Provenance.EventRef,
                sha = Provenance.EventSha,
                pullRequest
            },
            checkout = new { @ref = Provenance.CheckoutRef, sha = Provenance.CheckoutSha }
        };
        using var observedRun = JsonDocument.Parse(ObservedRun.ToJson(indented: false));
        var envelope = new
        {
            contractVersion = CurrentContractVersion,
            schema = CurrentSchema,
            observedRun = observedRun.RootElement,
            provenance
        };
        return JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = indented });
    }

    /// <summary>
    /// Reloads v3 evidence after revalidating the embedded provenance payload. The caller must
    /// independently re-check protected workflow-definition policy and supply that result;
    /// the trust decision is deliberately not serialized as self-authenticating evidence.
    /// </summary>
    public static CiWorkflowEvidencePacketV3 FromJson(
        string json,
        string expectedRepository,
        long expectedRunId,
        int expectedRunAttempt,
        string expectedPacketSha256,
        bool workflowDefinitionTrusted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPacketSha256);
        if (expectedPacketSha256.Length != 64 || !expectedPacketSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Expected packet digest must be a full SHA-256 hexadecimal value.", nameof(expectedPacketSha256));
        var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
        var actualPacketDigest = SHA256.HashData(jsonBytes);
        if (!CryptographicOperations.FixedTimeEquals(
            actualPacketDigest, Convert.FromHexString(expectedPacketSha256)))
            throw new InvalidDataException("Evidence packet v3 content digest does not match the trusted durable-store value.");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        ValidateNoDuplicateProperties(root);
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("contractVersion", out var version) ||
            !version.TryGetInt32(out var parsedVersion) ||
            parsedVersion != CurrentContractVersion ||
            !root.TryGetProperty("schema", out var schema) ||
            schema.ValueKind != JsonValueKind.String ||
            !string.Equals(schema.GetString(), CurrentSchema, StringComparison.Ordinal) ||
            !root.TryGetProperty("observedRun", out var observedRunElement) ||
            observedRunElement.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("provenance", out var provenanceElement) ||
            provenanceElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Evidence packet v3 contract version or schema is unsupported.");

        var observedRun = CiWorkflowEvidencePacket.FromJson(observedRunElement.GetRawText());
        var provenanceBytes = JsonSerializer.SerializeToUtf8Bytes(provenanceElement);
        var provenance = CiWorkflowProvenanceRecord.ParseAndValidate(
            provenanceBytes,
            expectedRepository,
            expectedRunId,
            expectedRunAttempt,
            Convert.ToHexString(SHA256.HashData(provenanceBytes)));

        return FromValidatedProvenance(
            observedRun,
            provenance,
            workflowDefinitionTrusted,
            observedRun.CandidateCheckoutEvidence);
    }

    private static void ValidateNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException($"Evidence packet v3 contains duplicate property '{property.Name}'.");
                ValidateNoDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                ValidateNoDuplicateProperties(item);
        }
    }

    private IEnumerable<string> BuildBlockingReasons()
    {
        foreach (var reason in ObservedRun.BlockingReasons)
        {
            // v2's candidate checkout bit is not authoritative for v3; replace it with
            // the independently checked provenance result below.
            if (!string.Equals(reason, "candidate.checkout-unverified", StringComparison.Ordinal))
                yield return reason;
        }

        if (!WorkflowDefinitionTrusted)
            yield return "workflow.definition-untrusted";
        if (!CheckoutVerified)
            yield return "candidate.checkout-unverified";
    }
}
