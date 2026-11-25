using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Canonical v1 evidence envelope. This contract requires a separately parsed,
/// repository/run/attempt-bound workflow provenance artifact. Raw provider run/job observations
/// remain unversioned facts nested inside this single envelope; workflow/candidate identity is
/// authoritative only in the separately validated provenance artifact.
/// </summary>
public sealed record CiWorkflowEvidencePacket
{
    public const int CurrentContractVersion = 1;
    public const string CurrentSchema = "ciai.workflow-evidence.v1";

    private CiWorkflowEvidencePacket(
        CiWorkflowRunEvidenceObservation observedRun,
        CiWorkflowProvenanceRecord provenance,
        bool workflowDefinitionTrusted,
        bool checkoutVerified,
        IReadOnlyList<CiWorkflowRequiredCheckObservation> requiredChecks)
    {
        ObservedRun = observedRun;
        Provenance = provenance;
        WorkflowDefinitionTrusted = workflowDefinitionTrusted;
        CheckoutVerified = checkoutVerified;
        RequiredChecks = Array.AsReadOnly(requiredChecks.OrderBy(check => check.Key, StringComparer.Ordinal).ToArray());
        ValidateRequiredCheckObservations(RequiredChecks);
        CandidateRef = provenance.CheckoutRef;
        CandidateSha = provenance.CheckoutSha;
        BlockingReasons = Array.AsReadOnly(BuildBlockingReasons().Distinct(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Creates v1 only from exact-run provenance already parsed by the trusted artifact retriever.
    /// The host must set workflowDefinitionTrusted only after independently matching WorkflowRef
    /// and WorkflowSha against its protected control-plane policy. This flag is not a substitute
    /// for that host-side verification.
    /// </summary>
    /// <summary>
    /// Builds v1 from the exact collected run/job attempt and a separately validated provenance
    /// artifact. The caller must obtain provenance through the trusted provider artifact boundary
    /// and apply protected workflow-definition policy before setting the trust argument.
    /// </summary>
    /// <summary>
    /// Builds evidence only after the caller has evaluated the exact workflow bytes against
    /// a protected control-plane revision. The decision must identify this exact provenance.
    /// </summary>
    public static CiWorkflowEvidencePacket FromCollections(
        CiWorkflowRunCollection runCollection,
        CiWorkflowJobCollection jobCollection,
        long runId,
        int runAttempt,
        DateTimeOffset observedAt,
        CiWorkflowProvenanceRecord provenance,
        CiWorkflowDefinitionTrustDecision trustDecision,
        IEnumerable<CiWorkflowRequiredCheckBinding> requiredChecks,
        CiWorkflowCandidateCheckoutEvidence? candidateCheckoutEvidence = null,
        IReadOnlyList<Uri>? artifactReferences = null,
        CiPullRequestProviderIdentity? pullRequestIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(trustDecision);
        ArgumentNullException.ThrowIfNull(provenance);
        if (!trustDecision.IsFor(provenance))
            throw new InvalidDataException("Workflow trust decision does not identify this exact provenance record.");
        ArgumentNullException.ThrowIfNull(requiredChecks);
        return FromCollections(
            runCollection, jobCollection, runId, runAttempt, observedAt, provenance,
            trustDecision.IsTrusted, requiredChecks, candidateCheckoutEvidence, artifactReferences, pullRequestIdentity);
    }

    public static CiWorkflowEvidencePacket FromCollections(
        CiWorkflowRunCollection runCollection,
        CiWorkflowJobCollection jobCollection,
        long runId,
        int runAttempt,
        DateTimeOffset observedAt,
        CiWorkflowProvenanceRecord provenance,
        bool workflowDefinitionTrusted,
        IEnumerable<CiWorkflowRequiredCheckBinding> requiredChecks,
        CiWorkflowCandidateCheckoutEvidence? candidateCheckoutEvidence = null,
        IReadOnlyList<Uri>? artifactReferences = null,
        CiPullRequestProviderIdentity? pullRequestIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(runCollection);
        ArgumentNullException.ThrowIfNull(jobCollection);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(requiredChecks);
        var requiredCheckBindings = requiredChecks.ToArray();
        ValidateRequiredCheckBindings(requiredCheckBindings);
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        if (runAttempt <= 0) throw new ArgumentOutOfRangeException(nameof(runAttempt));

        if (!string.Equals(runCollection.Repository, jobCollection.Repository, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Run and job collections belong to different repositories.");
        if (jobCollection.RunId != runId || jobCollection.RunAttempt != runAttempt)
            throw new InvalidDataException("Job collection does not identify the requested run attempt.");

        var matchingRuns = runCollection.Runs
            .Where(item => item.Run.RunId == runId && item.RunAttempt == runAttempt)
            .ToArray();
        if (matchingRuns.Length != 1)
            throw new InvalidDataException("Run collection must contain exactly one observation for the requested run attempt.");

        var observation = matchingRuns[0];
        if (!string.Equals(observation.Run.Repository, runCollection.Repository, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(observation.Run.HeadSha, runCollection.HeadSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Run observation does not match the collection repository and exact provider head SHA.");

        var workflowRefSeparator = provenance.WorkflowRef.LastIndexOf('@');
        if (workflowRefSeparator <= 0)
            throw new InvalidDataException("Validated workflow provenance does not contain a full workflow ref.");
        var provenanceWorkflowRef = provenance.WorkflowRef[(workflowRefSeparator + 1)..];
        var providerWorkflowRef = matchingRuns[0].WorkflowRef;
        if (string.IsNullOrWhiteSpace(providerWorkflowRef) ||
            !string.Equals(providerWorkflowRef, provenanceWorkflowRef, StringComparison.Ordinal))
            throw new InvalidDataException("Workflow provenance ref does not match the independently observed GitHub workflow ref.");
        var jobsComplete = !jobCollection.IsTruncated && jobCollection.TotalCount == jobCollection.Jobs.Count;
        var discoveryComplete = !runCollection.IsTruncated && runCollection.TotalCount == runCollection.Runs.Count;
        var observedRun = new CiWorkflowRunEvidenceObservation(
            observation.Run.Repository,
            observation.WorkflowPath,
            observation.Run.RunId,
            observation.RunAttempt,
            providerWorkflowRef!,
            observation.Run.HeadSha,
            observation.Event,
            observation.Run.WorkflowName,
            observation.Run.Status,
            observation.Run.Conclusion,
            observation.Run.EvidenceUrl,
            observedAt,
            jobCollection.Jobs,
            discoveryComplete,
            jobsComplete,
            artifactReferences,
            candidateCheckoutEvidence);

        if (provenance.PullRequest is not null)
        {
            var providerPullRequests = observation.PullRequestIdentities
                .Where(item => item.Number == provenance.PullRequest.Number).ToArray();
            if (observation.PullRequestIdentitiesComplete && providerPullRequests.Length == 1)
                observedRun = observedRun with { ProviderPullRequestObservation = providerPullRequests[0] };

            if (pullRequestIdentity is not null)
            {
                if (!observation.PullRequestIdentitiesComplete ||
                    !pullRequestIdentity.Matches(provenance) ||
                    providerPullRequests.Length != 1 ||
                    !pullRequestIdentity.Matches(providerPullRequests[0]))
                    throw new InvalidDataException("Supplied pull-request merge identity does not match the provider's exact head/base observation.");
                observedRun = observedRun with { ProviderPullRequestIdentity = pullRequestIdentity };
            }
        }
        else if (pullRequestIdentity is not null)
            throw new InvalidDataException("Pull-request provider identity was supplied for a non-pull-request run.");

        return FromValidatedProvenance(
            observedRun,
            provenance,
            workflowDefinitionTrusted,
            requiredCheckBindings,
            candidateCheckoutEvidence);
    }

    /// <summary>Builds evidence using a trust decision produced by the protected workflow-definition policy.</summary>
    public static CiWorkflowEvidencePacket FromValidatedProvenance(
        CiWorkflowRunEvidenceObservation observedRun,
        CiWorkflowProvenanceRecord provenance,
        CiWorkflowDefinitionTrustDecision trustDecision,
        IEnumerable<CiWorkflowRequiredCheckBinding> requiredChecks,
        CiPullRequestProviderIdentity? pullRequestIdentity = null,
        CiWorkflowCandidateCheckoutEvidence? candidateCheckoutEvidence = null)
    {
        ArgumentNullException.ThrowIfNull(trustDecision);
        ArgumentNullException.ThrowIfNull(provenance);
        if (!trustDecision.IsFor(provenance))
            throw new InvalidDataException("Workflow trust decision does not identify this exact provenance record.");
        ArgumentNullException.ThrowIfNull(requiredChecks);
        if (provenance.PullRequest is not null && pullRequestIdentity is not null)
        {
            if (!pullRequestIdentity.Matches(provenance) ||
                observedRun.ProviderPullRequestObservation is null ||
                !pullRequestIdentity.Matches(observedRun.ProviderPullRequestObservation))
                throw new InvalidDataException("Pull-request merge identity does not match the independently observed provider head/base facts.");
            observedRun = observedRun with { ProviderPullRequestIdentity = pullRequestIdentity };
        }
        else if (provenance.PullRequest is null && pullRequestIdentity is not null)
            throw new InvalidDataException("Pull-request provider identity was supplied for a non-pull-request run.");

        var pullRequestIdentityVerified = provenance.PullRequest is null ||
            observedRun.ProviderPullRequestObservation is not null &&
            observedRun.ProviderPullRequestIdentity is not null &&
            observedRun.ProviderPullRequestIdentity.Matches(provenance) &&
            observedRun.ProviderPullRequestIdentity.Matches(observedRun.ProviderPullRequestObservation);
        if (trustDecision.IsTrusted && !pullRequestIdentityVerified)
            throw new InvalidDataException("A trusted pull-request verdict requires independently verified head, base, merge commit, and merge-parent identities.");
        return FromValidatedProvenance(observedRun, provenance, trustDecision.IsTrusted, requiredChecks, candidateCheckoutEvidence);
    }

    public static CiWorkflowEvidencePacket FromValidatedProvenance(
        CiWorkflowRunEvidenceObservation observedRun,
        CiWorkflowProvenanceRecord provenance,
        bool workflowDefinitionTrusted,
        IEnumerable<CiWorkflowRequiredCheckBinding> requiredChecks,
        CiWorkflowCandidateCheckoutEvidence? candidateCheckoutEvidence = null)
    {
        ArgumentNullException.ThrowIfNull(observedRun);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(requiredChecks);
        var requiredCheckBindings = requiredChecks.ToArray();
        ValidateRequiredCheckBindings(requiredCheckBindings);

        if (!string.Equals(observedRun.Repository, provenance.Repository, StringComparison.OrdinalIgnoreCase) ||
            observedRun.RunId != provenance.RunId ||
            observedRun.RunAttempt != provenance.RunAttempt)
            throw new InvalidDataException("Workflow provenance does not identify the exact observed repository, run, and attempt.");

        // GitHub reports the PR source head in workflow-run head_sha, while a pull_request
        // workflow executes against the synthetic merge commit. Preserve both provider identities.
        var providerPullRequestMatches = provenance.PullRequest is null ||
            observedRun.ProviderPullRequestObservation is not null &&
            observedRun.ProviderPullRequestObservation.Number == provenance.PullRequest.Number &&
            string.Equals(observedRun.ProviderPullRequestObservation.HeadRef, provenance.PullRequest.HeadRef, StringComparison.Ordinal) &&
            string.Equals(observedRun.ProviderPullRequestObservation.HeadSha, provenance.PullRequest.HeadSha, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(observedRun.ProviderPullRequestObservation.BaseRef, provenance.PullRequest.BaseRef, StringComparison.Ordinal) &&
            string.Equals(observedRun.ProviderPullRequestObservation.BaseSha, provenance.PullRequest.BaseSha, StringComparison.OrdinalIgnoreCase);
        var providerHeadMatches = provenance.PullRequest is null
            ? string.Equals(observedRun.WorkflowSha, provenance.EventSha, StringComparison.OrdinalIgnoreCase)
            : providerPullRequestMatches &&
              string.Equals(observedRun.WorkflowSha, provenance.PullRequest.HeadSha, StringComparison.OrdinalIgnoreCase);
        if (provenance.PullRequest is null && !providerHeadMatches)
            throw new InvalidDataException("Workflow provenance does not match the independently observed provider head identity.");

        var pullRequestIdentityVerified = provenance.PullRequest is null ||
            providerPullRequestMatches &&
            observedRun.ProviderPullRequestIdentity is not null &&
            observedRun.ProviderPullRequestIdentity.Matches(provenance) &&
            observedRun.ProviderPullRequestObservation is not null &&
            observedRun.ProviderPullRequestIdentity.Matches(observedRun.ProviderPullRequestObservation);

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

            // The internal job observation is used only to corroborate the named resolution and
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

        if (provenance.PullRequest is not null)
            checkoutVerified = checkoutVerified && providerHeadMatches && pullRequestIdentityVerified;

        var requiredCheckObservations = BuildRequiredCheckObservations(observedRun, requiredCheckBindings);
        return new CiWorkflowEvidencePacket(
            observedRun, provenance, workflowDefinitionTrusted, checkoutVerified, requiredCheckObservations);
    }

    public int ContractVersion => CurrentContractVersion;
    public string Schema => CurrentSchema;
    public CiWorkflowRunEvidenceObservation ObservedRun { get; }
    public CiWorkflowProvenanceRecord Provenance { get; }
    public bool WorkflowDefinitionTrusted { get; }
    public string CandidateRef { get; }
    public string CandidateSha { get; }
    public bool CheckoutVerified { get; }
    public IReadOnlyList<CiWorkflowRequiredCheckObservation> RequiredChecks { get; }
    public bool PullRequestIdentityVerified => Provenance.PullRequest is null ||
        ObservedRun.ProviderPullRequestObservation is not null &&
        ObservedRun.ProviderPullRequestIdentity is not null &&
        ObservedRun.ProviderPullRequestIdentity.Matches(Provenance) &&
        ObservedRun.ProviderPullRequestIdentity.Matches(ObservedRun.ProviderPullRequestObservation);
    public bool EvidenceComplete =>
        ObservedRun.EvidenceComplete &&
        RequiredChecks.Count > 0 &&
        RequiredChecks.All(check =>
            check.State == "observed" &&
            check.JobId is > 0 &&
            check.Status == "completed" &&
            check.Conclusion is not null &&
            check.StartedAt.HasValue &&
            check.CompletedAt.HasValue &&
            check.CompletedAt.Value >= check.StartedAt.Value);

    public bool IsSuccessful =>
        EvidenceComplete &&
        WorkflowDefinitionTrusted &&
        CheckoutVerified &&
        ObservedRun.Conclusion == CiRunConclusion.Success &&
        RequiredChecks.All(check => check.Conclusion == "success");

    public bool HasActionableDiagnostics =>
        EvidenceComplete &&
        WorkflowDefinitionTrusted &&
        CheckoutVerified &&
        ObservedRun.Status == CiRunStatus.Completed &&
        ObservedRun.Conclusion == CiRunConclusion.Failure &&
        RequiredChecks.All(check => check.Conclusion is "success" or "failure") &&
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

    public string PayloadSha256 =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToJson(indented: false)))).ToLowerInvariant();

    public IReadOnlyList<string> BlockingReasons { get; }

    /// <summary>
    /// Serializes the v1 envelope without persisting the host's workflow-trust decision.
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
        var observedRun = JsonSerializer.SerializeToElement(ObservedRun, ObservationJsonOptions);
        var envelope = new
        {
            contractVersion = CurrentContractVersion,
            schema = CurrentSchema,
            observedRun,
            provenance,
            requiredChecks = RequiredChecks
        };
        return JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = indented });
    }

    /// <summary>
    /// Reloads v1 evidence after revalidating the embedded provenance payload. The caller must
    /// independently re-check protected workflow-definition policy and supply that result;
    /// the trust decision is deliberately not serialized as self-authenticating evidence.
    /// </summary>
    public static CiWorkflowEvidencePacket FromJson(
        string json,
        string expectedRepository,
        long expectedRunId,
        int expectedRunAttempt,
        string expectedPacketSha256,
        IEnumerable<CiWorkflowRequiredCheckBinding> expectedRequiredChecks,
        bool workflowDefinitionTrusted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(expectedRequiredChecks);
        var trustedRequiredChecks = expectedRequiredChecks.ToArray();
        ValidateRequiredCheckBindings(trustedRequiredChecks);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPacketSha256);
        if (expectedPacketSha256.Length != 64 || !expectedPacketSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Expected packet digest must be a full SHA-256 hexadecimal value.", nameof(expectedPacketSha256));
        var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
        var actualPacketDigest = SHA256.HashData(jsonBytes);
        if (!CryptographicOperations.FixedTimeEquals(
            actualPacketDigest, Convert.FromHexString(expectedPacketSha256)))
            throw new InvalidDataException("Evidence packet v1 content digest does not match the trusted durable-store value.");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        ValidateNoDuplicateProperties(root);
        ValidateKnownProperties(root,
            new HashSet<string>(StringComparer.Ordinal)
            {
                "contractVersion", "schema", "observedRun", "provenance", "requiredChecks"
            },
            "Evidence packet v1 envelope");
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
            provenanceElement.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("requiredChecks", out var requiredChecksElement) ||
            requiredChecksElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Evidence packet v1 contract version or schema is unsupported.");

        var observedRun = JsonSerializer.Deserialize<CiWorkflowRunEvidenceObservation>(
            observedRunElement.GetRawText(), ObservationJsonOptions)
            ?? throw new InvalidDataException("Evidence packet v1 observed-run data produced no observation.");
        var provenanceBytes = JsonSerializer.SerializeToUtf8Bytes(provenanceElement);
        var provenance = CiWorkflowProvenanceRecord.ParseAndValidate(
            provenanceBytes,
            expectedRepository,
            expectedRunId,
            expectedRunAttempt,
            Convert.ToHexString(SHA256.HashData(provenanceBytes)));

        var serializedRequiredChecks = JsonSerializer.Deserialize<CiWorkflowRequiredCheckObservation[]>(
            requiredChecksElement.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Evidence packet v1 required-check observations are invalid.");
        if (serializedRequiredChecks.Any(check => check is null))
            throw new InvalidDataException("Evidence packet v1 contains a null required-check observation.");
        foreach (var checkElement in requiredChecksElement.EnumerateArray())
            ValidateKnownProperties(checkElement,
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "key", "jobName", "state", "jobId", "status", "conclusion",
                    "evidenceUrl", "startedAt", "completedAt"
                },
                "Evidence packet v1 required-check observation");
        var serializedBindings = serializedRequiredChecks.Select(check =>
            new CiWorkflowRequiredCheckBinding(check.Key, check.JobName)).ToArray();
        try { ValidateRequiredCheckBindings(serializedBindings); }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Evidence packet v1 contains invalid required-check identities.", exception);
        }
        if (!trustedRequiredChecks.OrderBy(item => item.Key, StringComparer.Ordinal)
                .SequenceEqual(serializedBindings.OrderBy(item => item.Key, StringComparer.Ordinal)))
            throw new InvalidDataException("Evidence packet v1 required-check set does not match the trusted host registration.");

        var packet = FromValidatedProvenance(
            observedRun,
            provenance,
            workflowDefinitionTrusted,
            trustedRequiredChecks,
            observedRun.CandidateCheckoutEvidence);
        if (!packet.RequiredChecks.SequenceEqual(serializedRequiredChecks))
            throw new InvalidDataException("Evidence packet v1 required-check observations do not match provider-observed jobs.");
        return packet;
    }

    /// <summary>Reloads evidence using a host-issued protected workflow trust decision.</summary>
    public static CiWorkflowEvidencePacket FromJson(
        string json,
        string expectedRepository,
        long expectedRunId,
        int expectedRunAttempt,
        string expectedPacketSha256,
        IEnumerable<CiWorkflowRequiredCheckBinding> expectedRequiredChecks,
        CiWorkflowDefinitionTrustDecision trustDecision,
        CiPullRequestProviderIdentity? pullRequestIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(trustDecision);
        ArgumentNullException.ThrowIfNull(expectedRequiredChecks);
        var requiredCheckBindings = expectedRequiredChecks.ToArray();
        var observed = FromJson(
            json, expectedRepository, expectedRunId, expectedRunAttempt,
            expectedPacketSha256, requiredCheckBindings, workflowDefinitionTrusted: false);
        return FromValidatedProvenance(
            observed.ObservedRun,
            observed.Provenance,
            trustDecision,
            requiredCheckBindings,
            pullRequestIdentity,
            observed.ObservedRun.CandidateCheckoutEvidence);
    }

    private static JsonSerializerOptions ObservationJsonOptions => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static void ValidateNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException($"Evidence packet v1 contains duplicate property '{property.Name}'.");
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
            // the observation's candidate checkout bit is not authoritative for v1; replace it with
            // the independently checked provenance result below.
            if (!string.Equals(reason, "candidate.checkout-unverified", StringComparison.Ordinal))
                yield return reason;
        }

        if (!WorkflowDefinitionTrusted)
            yield return "workflow.definition-untrusted";
        if (Provenance.PullRequest is not null && !PullRequestIdentityVerified)
            yield return "pull-request.provider-identity-unverified";
        if (!CheckoutVerified)
            yield return "candidate.checkout-unverified";
        foreach (var check in RequiredChecks)
        {
            if (check.State == "missing") yield return "required-check.missing";
            else if (check.State == "ambiguous") yield return "required-check.ambiguous";
            else if (check.Status != "completed" || check.Conclusion is null ||
                !check.StartedAt.HasValue || !check.CompletedAt.HasValue)
                yield return "required-check.incomplete";
            else if (check.Conclusion != "success") yield return "required-check.non-success";
        }
    }

    private static void ValidateKnownProperties(
        JsonElement element,
        IReadOnlySet<string> allowedProperties,
        string scope)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{scope} must be a JSON object.");
        foreach (var property in element.EnumerateObject())
        {
            if (!allowedProperties.Contains(property.Name))
                throw new InvalidDataException($"{scope} contains unsupported property '{property.Name}'.");
        }
    }

    private static IReadOnlyList<CiWorkflowRequiredCheckObservation> BuildRequiredCheckObservations(
        CiWorkflowRunEvidenceObservation observedRun,
        IReadOnlyList<CiWorkflowRequiredCheckBinding> bindings)
    {
        return Array.AsReadOnly(bindings.OrderBy(binding => binding.Key, StringComparer.Ordinal).Select(binding =>
        {
            var matches = observedRun.Jobs.Where(job =>
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
        }).ToArray());
    }

    private static void ValidateRequiredCheckBindings(IReadOnlyList<CiWorkflowRequiredCheckBinding> bindings)
    {
        if (bindings.Count == 0 || bindings.Any(binding =>
            binding is null || string.IsNullOrWhiteSpace(binding.Key) || binding.Key.Length > 100 ||
            !binding.Key.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.') ||
            string.IsNullOrWhiteSpace(binding.JobName) || binding.JobName.Length > 200 ||
            binding.JobName.Any(char.IsControl)))
            throw new ArgumentException("At least one valid required-check key and exact provider job name is required.", nameof(bindings));
        if (bindings.Select(binding => binding.Key).Distinct(StringComparer.Ordinal).Count() != bindings.Count ||
            bindings.Select(binding => binding.JobName).Distinct(StringComparer.Ordinal).Count() != bindings.Count)
            throw new ArgumentException("Required-check keys and provider job names must be unique.", nameof(bindings));
    }

    private static void ValidateRequiredCheckObservations(IReadOnlyList<CiWorkflowRequiredCheckObservation> checks)
    {
        if (checks.Any(check => check is null))
            throw new InvalidDataException("Evidence packet v1 contains a null required-check observation.");
        ValidateRequiredCheckBindings(checks.Select(check =>
            new CiWorkflowRequiredCheckBinding(check.Key, check.JobName)).ToArray());
        foreach (var check in checks)
        {
            if (check.State is not ("observed" or "missing" or "ambiguous"))
                throw new InvalidDataException("Evidence packet v1 contains an unknown required-check observation state.");
            if (check.State == "observed")
            {
                if (check.JobId is not > 0 || check.Status is null ||
                    (check.Conclusion is not null && !IsKnownConclusion(check.Conclusion)) ||
                    check.EvidenceUrl is null || !IsSafeGitHubUrl(check.EvidenceUrl) ||
                    !check.StartedAt.HasValue || !check.CompletedAt.HasValue ||
                    check.CompletedAt.Value < check.StartedAt.Value)
                    throw new InvalidDataException("Evidence packet v1 contains malformed provider job evidence.");
            }
            else if (check.JobId.HasValue || check.Status is not null || check.Conclusion is not null ||
                check.EvidenceUrl is not null || check.StartedAt.HasValue || check.CompletedAt.HasValue)
                throw new InvalidDataException("Missing or ambiguous required checks cannot carry unverified job identity.");
        }
    }

    private static bool IsKnownConclusion(string value) =>
        value is "success" or "failure" or "cancelled" or "timed_out" or "action_required" or
            "neutral" or "skipped" or "stale" or "startup_failure";

    private static bool IsSafeGitHubUrl(Uri uri) =>
        uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment);
}

public sealed record CiWorkflowRequiredCheckBinding(string Key, string JobName);

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
