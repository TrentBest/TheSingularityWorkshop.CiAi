namespace TheSingularityWorkshop.CiAi;

/// <summary>Verification state supplied by the host's authoritative entitlement provider.</summary>
public enum CiAiEntitlementVerificationStatus
{
    Verified = 0,
    Unavailable = 1
}

/// <summary>Result of attempting to admit a repository under a CiAi entitlement.</summary>
public enum CiAiRepositoryAdmissionOutcome
{
    Admitted = 0,
    UpgradeRequired = 1,
    EntitlementUnavailable = 2,
    InvalidRequest = 3
}

/// <summary>
/// A verified entitlement snapshot. Construct this only from an authenticated,
/// authoritative entitlement source; a local repository manifest is not an entitlement.
/// </summary>
public sealed record CiAiRepositoryEntitlement
{
    public CiAiRepositoryEntitlement(string licenseeId, string tier, int maximumConfiguredRepositories,
        DateTimeOffset validFromUtc, DateTimeOffset? validUntilUtc,
        CiAiEntitlementVerificationStatus verificationStatus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tier);
        if (maximumConfiguredRepositories < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumConfiguredRepositories));
        if (validFromUtc.Offset != TimeSpan.Zero || (validUntilUtc is not null && validUntilUtc.Value.Offset != TimeSpan.Zero))
            throw new ArgumentException("Entitlement validity timestamps must be UTC.");
        if (validUntilUtc is not null && validUntilUtc.Value <= validFromUtc)
            throw new ArgumentException("Entitlement expiry must be later than its start.", nameof(validUntilUtc));
        if (!Enum.IsDefined(verificationStatus))
            throw new ArgumentOutOfRangeException(nameof(verificationStatus));

        LicenseeId = licenseeId.Trim();
        Tier = tier.Trim();
        MaximumConfiguredRepositories = maximumConfiguredRepositories;
        ValidFromUtc = validFromUtc;
        ValidUntilUtc = validUntilUtc;
        VerificationStatus = verificationStatus;
    }

    public string LicenseeId { get; }
    public string Tier { get; }
    public int MaximumConfiguredRepositories { get; }
    public DateTimeOffset ValidFromUtc { get; }
    public DateTimeOffset? ValidUntilUtc { get; }
    public CiAiEntitlementVerificationStatus VerificationStatus { get; }
}

/// <summary>Immutable input snapshot for one repository admission attempt.</summary>
public sealed record CiAiRepositoryAdmissionRequest
{
    public CiAiRepositoryAdmissionRequest(string? candidateRepository, IEnumerable<string> configuredRepositories,
        string correlationId, DateTimeOffset requestedAtUtc, Uri? upgradeUrl = null)
    {
        ArgumentNullException.ThrowIfNull(configuredRepositories);
        if (requestedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Admission timestamp must be UTC.", nameof(requestedAtUtc));
        if (upgradeUrl is not null &&
            (upgradeUrl.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(upgradeUrl.UserInfo) ||
             !string.IsNullOrEmpty(upgradeUrl.Fragment)))
            throw new ArgumentException("Upgrade URL must be an HTTPS URL without credentials or a fragment.", nameof(upgradeUrl));

        CandidateRepository = candidateRepository;
        ConfiguredRepositories = Array.AsReadOnly(configuredRepositories.ToArray());
        CorrelationId = correlationId;
        RequestedAtUtc = requestedAtUtc;
        UpgradeUrl = upgradeUrl;
    }

    public string? CandidateRepository { get; }
    public IReadOnlyList<string> ConfiguredRepositories { get; }
    public string CorrelationId { get; }
    public DateTimeOffset RequestedAtUtc { get; }
    public Uri? UpgradeUrl { get; }
}

/// <summary>
/// Deterministic admission decision. This evaluator does not reserve a slot or
/// provide concurrency control; the host must enforce check-and-enroll atomically.
/// </summary>
public sealed record CiAiRepositoryAdmissionReport
{
    internal CiAiRepositoryAdmissionReport(CiAiRepositoryAdmissionOutcome outcome, string resultCode,
        string message, string? licenseeId, string? candidateRepository, int configuredRepositoryCount,
        int projectedConfiguredRepositoryCount, int maximumAuthorizedRepositories, string? tier, string? entitlementStatus,
        DateTimeOffset evaluatedAtUtc, string correlationId, Uri? upgradeUrl)
    {
        Outcome = outcome;
        ResultCode = resultCode;
        Message = message;
        LicenseeId = licenseeId;
        CandidateRepository = candidateRepository;
        ConfiguredRepositoryCount = configuredRepositoryCount;
        ProjectedConfiguredRepositoryCount = projectedConfiguredRepositoryCount;
        MaximumAuthorizedRepositories = maximumAuthorizedRepositories;
        Tier = tier;
        EntitlementStatus = entitlementStatus;
        EvaluatedAtUtc = evaluatedAtUtc;
        CorrelationId = correlationId;
        UpgradeUrl = upgradeUrl;
        NoWorkStarted = outcome != CiAiRepositoryAdmissionOutcome.Admitted;
    }

    public CiAiRepositoryAdmissionOutcome Outcome { get; }
    public string ResultCode { get; }
    public string Message { get; }
    public string? LicenseeId { get; }
    public string? CandidateRepository { get; }
    public int ConfiguredRepositoryCount { get; }
    public int ProjectedConfiguredRepositoryCount { get; }
    public int MaximumAuthorizedRepositories { get; }
    public string? Tier { get; }
    public string? EntitlementStatus { get; }
    public DateTimeOffset EvaluatedAtUtc { get; }
    public string CorrelationId { get; }
    public Uri? UpgradeUrl { get; }
    public bool NoWorkStarted { get; }
    public bool MayStartWork => Outcome == CiAiRepositoryAdmissionOutcome.Admitted;
}

/// <summary>Pure, fail-closed evaluator for CiAi repository admission.</summary>
public static class CiAiRepositoryAdmissionEvaluator
{
    public const int FreeRepositoryLimit = 5;
    public const string RepositoryLimitExceededCode = "CIAI_LICENSE_REPOSITORY_LIMIT_EXCEEDED";
    public const string EntitlementUnavailableCode = "CIAI_LICENSE_ENTITLEMENT_UNAVAILABLE";

    public static CiAiRepositoryAdmissionReport Evaluate(CiAiRepositoryAdmissionRequest request,
        CiAiRepositoryEntitlement entitlement)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(entitlement);

        var candidate = request.CandidateRepository;
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? "missing-correlation-id" : request.CorrelationId.Trim();
        var configured = request.ConfiguredRepositories;
        var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!RepositoryIdentity.IsValid(candidate) || string.IsNullOrWhiteSpace(request.CorrelationId) ||
            configured.Any(repository => !RepositoryIdentity.IsValid(repository)))
            return Create(CiAiRepositoryAdmissionOutcome.InvalidRequest, "CIAI_LICENSE_ADMISSION_INVALID_REQUEST",
                "The repository admission request contains an invalid repository identity or correlation ID.",
                entitlement, candidate, 0, 0, request, correlationId, null);

        foreach (var repository in configured)
            distinct.Add(repository);

        var count = distinct.Count;
        if (distinct.Contains(candidate!))
            return Create(CiAiRepositoryAdmissionOutcome.Admitted, "CIAI_REPOSITORY_ALREADY_ADMITTED",
                "The repository is already configured; admission is idempotent and consumes no additional repository slot.",
                entitlement, candidate, count, count, request, correlationId, null);

        var now = request.RequestedAtUtc;
        var verified = entitlement.VerificationStatus == CiAiEntitlementVerificationStatus.Verified &&
            now >= entitlement.ValidFromUtc &&
            (entitlement.ValidUntilUtc is null || now < entitlement.ValidUntilUtc.Value);
        if (!verified)
            return Create(CiAiRepositoryAdmissionOutcome.EntitlementUnavailable, EntitlementUnavailableCode,
                "CiAi could not verify a currently valid entitlement. The new repository was not admitted; existing repositories are unaffected.",
                entitlement, candidate, count, count, request, correlationId, request.UpgradeUrl);

        if (count >= entitlement.MaximumConfiguredRepositories)
            return Create(CiAiRepositoryAdmissionOutcome.UpgradeRequired, RepositoryLimitExceededCode,
                $"CiAi did not enroll this repository. This account has {count} configured repositories and is authorized for {entitlement.MaximumConfiguredRepositories}. A paid license is required to add another repository. No CiAi work was started for this repository.",
                entitlement, candidate, count, count, request, correlationId, request.UpgradeUrl);

        return Create(CiAiRepositoryAdmissionOutcome.Admitted, "CIAI_REPOSITORY_ADMITTED",
            "The repository fits within the verified CiAi repository allowance.",
            entitlement, candidate, count, count + 1, request, correlationId, null);
    }

    private static CiAiRepositoryAdmissionReport Create(CiAiRepositoryAdmissionOutcome outcome, string code,
        string message, CiAiRepositoryEntitlement entitlement, string? candidate, int count,
        int projectedCount, CiAiRepositoryAdmissionRequest request, string correlationId, Uri? upgradeUrl) =>
        new(outcome, code, message, entitlement.LicenseeId, candidate, count, projectedCount,
            entitlement.MaximumConfiguredRepositories, entitlement.Tier,
            entitlement.VerificationStatus.ToString(), request.RequestedAtUtc,
            correlationId, upgradeUrl);
}
