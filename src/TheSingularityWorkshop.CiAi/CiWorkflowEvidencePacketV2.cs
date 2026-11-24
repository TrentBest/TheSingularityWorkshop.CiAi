using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Authoritative v2 envelope for one exact workflow run attempt. It wraps the independently
/// observed v1 run/provenance packet with the host-registered required-check identities.
/// The host must re-evaluate workflow-definition trust when loading the packet.
/// </summary>
public sealed record CiWorkflowEvidencePacketV2
{
    public const int CurrentContractVersion = 2;
    public const string CurrentSchema = "ciai.workflow-evidence.v2";

    private CiWorkflowEvidencePacketV2(
        CiWorkflowEvidencePacket workflowEvidence,
        IReadOnlyList<CiWorkflowRequiredCheckObservation> requiredChecks)
    {
        WorkflowEvidence = workflowEvidence;
        RequiredChecks = Array.AsReadOnly(requiredChecks.ToArray());
        Validate();
    }

    public CiWorkflowEvidencePacket WorkflowEvidence { get; }
    public IReadOnlyList<CiWorkflowRequiredCheckObservation> RequiredChecks { get; }
    public string Repository => WorkflowEvidence.ObservedRun.Repository;
    public string WorkflowPath => WorkflowEvidence.ObservedRun.WorkflowPath;
    public long RunId => WorkflowEvidence.ObservedRun.RunId;
    public int RunAttempt => WorkflowEvidence.ObservedRun.RunAttempt;
    public string CandidateRef => WorkflowEvidence.CandidateRef;
    public string CandidateSha => WorkflowEvidence.CandidateSha;
    public DateTimeOffset ObservedAt => WorkflowEvidence.ObservedRun.ObservedAt;
    public DateTimeOffset StartedAt => RequiredChecks
        .Where(check => check.StartedAt.HasValue)
        .Select(check => check.StartedAt!.Value)
        .DefaultIfEmpty(ObservedAt)
        .Min();

    public bool EvidenceComplete =>
        WorkflowEvidence.EvidenceComplete &&
        RequiredChecks.Count > 0 &&
        RequiredChecks.All(check =>
            check.State == "observed" &&
            check.JobId is > 0 &&
            check.Status == "completed" &&
            check.Conclusion is not null &&
            check.StartedAt.HasValue &&
            check.CompletedAt.HasValue &&
            check.CompletedAt >= check.StartedAt);

    public bool IsSuccessful =>
        EvidenceComplete &&
        WorkflowEvidence.WorkflowDefinitionTrusted &&
        WorkflowEvidence.CheckoutVerified &&
        WorkflowEvidence.ObservedRun.Conclusion == CiRunConclusion.Success &&
        RequiredChecks.All(check => check.Conclusion == "success");

    public bool HasActionableDiagnostics =>
        EvidenceComplete &&
        WorkflowEvidence.WorkflowDefinitionTrusted &&
        WorkflowEvidence.CheckoutVerified &&
        WorkflowEvidence.ObservedRun.Status == CiRunStatus.Completed &&
        WorkflowEvidence.ObservedRun.Conclusion == CiRunConclusion.Failure &&
        RequiredChecks.Any(check => check.Status == "completed" && check.Conclusion == "failure");

    public string? DiagnosticsFingerprint
    {
        get
        {
            if (!HasActionableDiagnostics)
                return null;

            var material = string.Join("\n", RequiredChecks
                .OrderBy(check => check.Key, StringComparer.Ordinal)
                .Select(check => string.Join("|",
                    check.Key,
                    check.JobId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    check.Status,
                    check.Conclusion)));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        }
    }

    /// <summary>
    /// Creates v2 from a provider-observed v1 packet and the required-check identities registered
    /// by the trusted host before dispatch. Missing or ambiguous jobs remain explicit and fail closed.
    /// </summary>
    public static CiWorkflowEvidencePacketV2 FromPacket(
        CiWorkflowEvidencePacket workflowEvidence,
        IEnumerable<CiWorkflowRequiredCheckBinding> requiredChecks)
    {
        ArgumentNullException.ThrowIfNull(workflowEvidence);
        ArgumentNullException.ThrowIfNull(requiredChecks);

        var bindings = requiredChecks.ToArray();
        ValidateBindings(bindings);
        var observed = workflowEvidence.ObservedRun.Jobs;
        var results = bindings.Select(binding =>
        {
            var matches = observed.Where(job =>
                string.Equals(job.Name, binding.JobName, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 0)
                return new CiWorkflowRequiredCheckObservation(
                    binding.Key, binding.JobName, "missing", null, null, null, null, null, null);
            if (matches.Length > 1)
                return new CiWorkflowRequiredCheckObservation(
                    binding.Key, binding.JobName, "ambiguous", null, null, null, null, null, null);

            var job = matches[0];
            return new CiWorkflowRequiredCheckObservation(
                binding.Key, binding.JobName, "observed", job.JobId, job.Status, job.Conclusion,
                job.EvidenceUrl, job.StartedAt, job.CompletedAt);
        }).ToArray();

        return new CiWorkflowEvidencePacketV2(workflowEvidence, results);
    }

    /// <summary>Serializes the exact v2 envelope. Host trust is deliberately not serialized.</summary>
    public string ToJson(bool indented = true)
    {
        Validate();
        using var workflowDocument = JsonDocument.Parse(WorkflowEvidence.ToJson(indented: false));
        var envelope = new
        {
            contractVersion = CurrentContractVersion,
            schema = CurrentSchema,
            workflowEvidence = workflowDocument.RootElement,
            requiredChecks = RequiredChecks
        };
        return JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = indented });
    }

    /// <summary>SHA-256 of the exact compact UTF-8 v2 payload bytes.</summary>
    public string PayloadSha256 =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToJson(indented: false)))).ToLowerInvariant();

    /// <summary>
    /// Parses v2 only after checking the durable payload digest and exact run identity. Workflow
    /// trust is supplied by the host and re-evaluated on every load.
    /// </summary>
    public static CiWorkflowEvidencePacketV2 FromJson(
        string json,
        string expectedRepository,
        long expectedRunId,
        int expectedRunAttempt,
        string expectedPayloadSha256,
        bool workflowDefinitionTrusted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRepository);
        if (expectedRunId <= 0 || expectedRunAttempt <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedRunId));
        if (expectedPayloadSha256 is not { Length: 64 } || !expectedPayloadSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Expected v2 payload digest must be a full SHA-256 hexadecimal value.", nameof(expectedPayloadSha256));

        var bytes = Encoding.UTF8.GetBytes(json);
        var actualDigest = SHA256.HashData(bytes);
        if (!CryptographicOperations.FixedTimeEquals(actualDigest, Convert.FromHexString(expectedPayloadSha256)))
            throw new InvalidDataException("Workflow evidence v2 payload digest does not match the trusted durable-store value.");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        ValidateNoDuplicateProperties(root);
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("contractVersion", out var version) ||
            !version.TryGetInt32(out var parsedVersion) || parsedVersion != CurrentContractVersion ||
            !root.TryGetProperty("schema", out var schema) ||
            schema.ValueKind != JsonValueKind.String ||
            !string.Equals(schema.GetString(), CurrentSchema, StringComparison.Ordinal) ||
            !root.TryGetProperty("workflowEvidence", out var workflowElement) ||
            workflowElement.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("requiredChecks", out var checksElement) ||
            checksElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Workflow evidence v2 schema or envelope is invalid.");

        var workflowJson = workflowElement.GetRawText();
        var workflowDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(workflowJson))).ToLowerInvariant();
        var workflowEvidence = CiWorkflowEvidencePacket.FromJson(
            workflowJson, expectedRepository, expectedRunId, expectedRunAttempt,
            workflowDigest, workflowDefinitionTrusted);

        var checks = JsonSerializer.Deserialize<CiWorkflowRequiredCheckObservation[]>(
            checksElement.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Workflow evidence v2 required-check list is invalid.");

        var packet = new CiWorkflowEvidencePacketV2(workflowEvidence, checks);
        if (!string.Equals(packet.Repository, expectedRepository, StringComparison.OrdinalIgnoreCase) ||
            packet.RunId != expectedRunId || packet.RunAttempt != expectedRunAttempt ||
            !string.Equals(packet.PayloadSha256, expectedPayloadSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Workflow evidence v2 does not match the expected repository, run, attempt, or canonical payload digest.");
        return packet;
    }

    private void Validate()
    {
        if (WorkflowEvidence is null)
            throw new ArgumentNullException(nameof(WorkflowEvidence));
        ValidateBindings(RequiredChecks.Select(check =>
            new CiWorkflowRequiredCheckBinding(check.Key, check.JobName)).ToArray());

        foreach (var check in RequiredChecks)
        {
            if (check.State is not ("observed" or "missing" or "ambiguous"))
                throw new InvalidDataException("Workflow evidence v2 contains an unknown required-check observation state.");
            if (check.State == "observed")
            {
                if (check.JobId is not > 0 || check.Status is null ||
                    (check.Conclusion is not null && !IsKnownConclusion(check.Conclusion)) ||
                    check.EvidenceUrl is null || !IsSafeGitHubUrl(check.EvidenceUrl) ||
                    !check.StartedAt.HasValue || !check.CompletedAt.HasValue ||
                    check.CompletedAt < check.StartedAt)
                    throw new InvalidDataException("Workflow evidence v2 contains malformed provider job evidence.");
            }
            else if (check.JobId.HasValue || check.Status is not null || check.Conclusion is not null ||
                check.EvidenceUrl is not null || check.StartedAt.HasValue || check.CompletedAt.HasValue)
                throw new InvalidDataException("Missing or ambiguous required checks cannot carry an unverified job identity.");
        }
    }

    private static void ValidateBindings(IReadOnlyList<CiWorkflowRequiredCheckBinding> bindings)
    {
        if (bindings.Count == 0 || bindings.Any(binding =>
            binding is null || string.IsNullOrWhiteSpace(binding.Key) || binding.Key.Length > 100 ||
            !binding.Key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') ||
            string.IsNullOrWhiteSpace(binding.JobName) || binding.JobName.Length > 200 ||
            binding.JobName.Any(char.IsControl)))
            throw new ArgumentException("At least one valid required-check key and exact provider job name is required.", nameof(bindings));
        if (bindings.Select(binding => binding.Key).Distinct(StringComparer.Ordinal).Count() != bindings.Count ||
            bindings.Select(binding => binding.JobName).Distinct(StringComparer.Ordinal).Count() != bindings.Count)
            throw new ArgumentException("Required-check keys and provider job names must be unique.", nameof(bindings));
    }

    private static bool IsKnownConclusion(string value) =>
        value is "success" or "failure" or "cancelled" or "timed_out" or "action_required" or
            "neutral" or "skipped" or "stale" or "startup_failure";

    private static bool IsSafeGitHubUrl(Uri uri) =>
        uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment);

    private static void ValidateNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException($"Workflow evidence v2 contains duplicate property '{property.Name}'.");
                ValidateNoDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                ValidateNoDuplicateProperties(item);
        }
    }
}

/// <summary>Trusted host registration for one required provider job.</summary>
public sealed record CiWorkflowRequiredCheckBinding(string Key, string JobName);

/// <summary>Provider-observed result for one required job, or an explicit missing/ambiguous state.</summary>
public sealed record CiWorkflowRequiredCheckObservation(
    string Key,
    string JobName,
    string State,
    long? JobId,
    string? Status,
    string? Conclusion,
    Uri? EvidenceUrl,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);
