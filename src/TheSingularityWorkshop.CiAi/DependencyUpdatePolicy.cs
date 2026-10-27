namespace TheSingularityWorkshop.CiAi;

/// <summary>Controls what CiAi may do after an upstream package update is observed.</summary>
/// <remarks>
/// This policy is a decision contract, not a feed monitor or updater. It never edits project files,
/// invokes builds, or publishes packages. Version classification and compatibility evidence must
/// come from separate, attributable observations; a semantic-version label is not compatibility proof.
/// </remarks>
public sealed record DependencyUpdatePolicy
{
    public DependencyUpdatePolicy(
        string packageId,
        DependencyUpdateMode mode,
        DependencyUpdateScope allowedScope = DependencyUpdateScope.Patch,
        bool allowPrerelease = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        if (!string.Equals(packageId, packageId.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Package ID cannot have leading or trailing whitespace.", nameof(packageId));
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (!Enum.IsDefined(allowedScope))
            throw new ArgumentOutOfRangeException(nameof(allowedScope));

        PackageId = packageId;
        Mode = mode;
        AllowedScope = allowedScope;
        AllowPrerelease = allowPrerelease;
    }

    public string PackageId { get; }
    public DependencyUpdateMode Mode { get; }
    public DependencyUpdateScope AllowedScope { get; }
    public bool AllowPrerelease { get; }

    /// <summary>
    /// Evaluates policy against a separately established update classification. This does not
    /// establish compatibility or authorize a downstream release.
    /// </summary>
    public DependencyUpdatePolicyDecision Evaluate(DependencyUpdateCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (!string.Equals(candidate.PackageId, PackageId, StringComparison.Ordinal))
            return new(DependencyUpdateDisposition.Blocked, "dependency.package-id-mismatch");

        if (!candidate.IsNewerVersion)
            return new(DependencyUpdateDisposition.Blocked, "dependency.candidate-not-newer");

        if (candidate.IsPrerelease && !AllowPrerelease)
            return new(DependencyUpdateDisposition.Blocked, "dependency.prerelease-not-allowed");

        if (!IsWithinScope(candidate.Scope))
            return new(DependencyUpdateDisposition.Blocked, "dependency.update-outside-policy");

        return Mode switch
        {
            DependencyUpdateMode.MonitorOnly =>
                new(DependencyUpdateDisposition.ReportOnly, "dependency.update-observed"),
            DependencyUpdateMode.RequireApproval =>
                new(DependencyUpdateDisposition.AwaitingApproval, "dependency.approval-required"),
            DependencyUpdateMode.AutomaticWithinPolicy =>
                new(DependencyUpdateDisposition.EligibleForValidation, "dependency.validation-required"),
            _ => new(DependencyUpdateDisposition.Blocked, "dependency.unknown-policy-mode")
        };
    }

    private bool IsWithinScope(DependencyUpdateScope candidateScope) => AllowedScope switch
    {
        DependencyUpdateScope.Patch => candidateScope == DependencyUpdateScope.Patch,
        DependencyUpdateScope.MinorOrPatch =>
            candidateScope is DependencyUpdateScope.Patch or DependencyUpdateScope.Minor,
        DependencyUpdateScope.AnyStableUpdate =>
            candidateScope is DependencyUpdateScope.Patch or DependencyUpdateScope.Minor or DependencyUpdateScope.Major,
        _ => false
    };
}

/// <summary>How a policy handles an observed upstream release.</summary>
public enum DependencyUpdateMode
{
    /// <summary>Report the update without changing dependencies or running adoption work.</summary>
    MonitorOnly = 0,
    /// <summary>Require a separate owner or configured approver decision before adoption.</summary>
    RequireApproval = 1,
    /// <summary>Permit in-policy candidates to proceed to validation; never grants publish permission.</summary>
    AutomaticWithinPolicy = 2
}

/// <summary>The independently established compatibility/version lane of a candidate.</summary>
public enum DependencyUpdateScope
{
    Patch = 0,
    Minor = 1,
    Major = 2,
    Unknown = 3
}

/// <summary>Evidence-bearing observation supplied to a dependency policy.</summary>
public sealed record DependencyUpdateCandidate(
    string PackageId,
    string CurrentVersion,
    string CandidateVersion,
    DependencyUpdateScope Scope,
    bool IsNewerVersion,
    bool IsPrerelease);

/// <summary>Policy outcome. Eligibility for validation is not evidence of compatibility or release authorization.</summary>
public sealed record DependencyUpdatePolicyDecision(
    DependencyUpdateDisposition Disposition,
    string ReasonCode);

public enum DependencyUpdateDisposition
{
    Blocked = 0,
    ReportOnly = 1,
    AwaitingApproval = 2,
    EligibleForValidation = 3
}
