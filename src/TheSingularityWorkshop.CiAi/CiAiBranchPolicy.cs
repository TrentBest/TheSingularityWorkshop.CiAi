namespace TheSingularityWorkshop.CiAi;

/// <summary>Branch-strategy preference. It never grants write, release, or deployment authority.</summary>
public enum CiAiBranchPolicyKind
{
    Sith = 0,
    Custom = 1,
    Disabled = 2
}

public enum CiAiBranchPolicyOutcome
{
    Compliant = 0,
    Deviation = 1,
    Unknown = 2,
    NotApplicable = 3,
    InvalidConfiguration = 4
}

/// <summary>A validated, named branch-strategy profile.</summary>
public sealed record CiAiBranchPolicyProfile
{
    private CiAiBranchPolicyProfile(string name, CiAiBranchPolicyKind kind, IEnumerable<string> expectedBranches)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 80 || name.Any(char.IsControl))
            throw new ArgumentException("Branch-policy profile name is invalid.", nameof(name));
        var branches = expectedBranches.ToArray();
        if (kind == CiAiBranchPolicyKind.Custom &&
            (branches.Length == 0 || branches.Any(x => !IsValidBranchName(x)) ||
             branches.Distinct(StringComparer.Ordinal).Count() != branches.Length))
            throw new ArgumentException("A custom profile requires unique, valid expected branch names.", nameof(expectedBranches));
        if (kind != CiAiBranchPolicyKind.Custom && branches.Length != 0)
            throw new ArgumentException("Only custom profiles may supply expected branches.", nameof(expectedBranches));
        Name = name;
        Kind = kind;
        ExpectedBranches = Array.AsReadOnly(branches.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    public string Name { get; }
    public CiAiBranchPolicyKind Kind { get; }
    public IReadOnlyList<string> ExpectedBranches { get; }

    public static CiAiBranchPolicyProfile SithPolicy() =>
        new("SithPolicy", CiAiBranchPolicyKind.Sith, Array.Empty<string>());

    public static CiAiBranchPolicyProfile Disabled() =>
        new("Disabled", CiAiBranchPolicyKind.Disabled, Array.Empty<string>());

    public static CiAiBranchPolicyProfile Custom(string name, IEnumerable<string> expectedBranches)
    {
        ArgumentNullException.ThrowIfNull(expectedBranches);
        return new CiAiBranchPolicyProfile(name, CiAiBranchPolicyKind.Custom, expectedBranches);
    }

    private static bool IsValidBranchName(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 250 &&
        !value.StartsWith('/') && !value.EndsWith('/') && !value.EndsWith('.') &&
        !value.Contains("..", StringComparison.Ordinal) &&
        !value.Contains("//", StringComparison.Ordinal) &&
        value.All(c => !char.IsControl(c) && !char.IsWhiteSpace(c) &&
            c is not '~' and not '^' and not ':' and not '?' and not '*' and not '[' and not '\\' and not '@' and not '{');
}

/// <summary>
/// A branch-policy selection loaded by the trusted CiAi host. The host must verify the source
/// configuration's authority before constructing this value; this data object does not authenticate itself.
/// </summary>
public sealed record CiAiBranchPolicyConfiguration
{
    public const string CurrentSchema = "ciai.branch-policy.v1";

    public CiAiBranchPolicyConfiguration(string repository, string targetCommitSha,
        string configurationSourceCommitSha, CiAiBranchPolicyProfile profile)
    {
        if (!IsRepositoryIdentity(repository))
            throw new ArgumentException("Repository must use canonical owner/repository identity.", nameof(repository));
        if (!IsFullSha(targetCommitSha))
            throw new ArgumentException("Target commit must be a full 40-character SHA.", nameof(targetCommitSha));
        if (!IsFullSha(configurationSourceCommitSha))
            throw new ArgumentException("Configuration source commit must be a full 40-character SHA.", nameof(configurationSourceCommitSha));
        ArgumentNullException.ThrowIfNull(profile);
        Repository = repository;
        TargetCommitSha = targetCommitSha.ToLowerInvariant();
        ConfigurationSourceCommitSha = configurationSourceCommitSha.ToLowerInvariant();
        Profile = profile;
    }

    public string Schema => CurrentSchema;
    public string Repository { get; }
    public string TargetCommitSha { get; }
    public string ConfigurationSourceCommitSha { get; }
    public CiAiBranchPolicyProfile Profile { get; }

    internal static bool IsFullSha(string value) =>
        value is { Length: 40 } && value.All(Uri.IsHexDigit);

    internal static bool IsRepositoryIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200) return false;
        var parts = value.Split('/');
        return parts.Length == 2 && parts.All(part => part.Length is > 0 and <= 100 &&
            part.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'));
    }
}

/// <summary>One complete or explicitly unavailable branch inventory.</summary>
public sealed record CiAiBranchPolicyInventory
{
    private CiAiBranchPolicyInventory(bool isComplete, IEnumerable<string> branches, string? failureReason)
    {
        var values = branches.ToArray();
        if (values.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A complete branch inventory cannot contain blank branch names.", nameof(branches));
        IsComplete = isComplete;
        Branches = Array.AsReadOnly(values.Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal).ToArray());
        FailureReason = failureReason;
    }

    public bool IsComplete { get; }
    public IReadOnlyList<string> Branches { get; }
    public string? FailureReason { get; }

    public static CiAiBranchPolicyInventory Complete(IEnumerable<string> branches)
    {
        ArgumentNullException.ThrowIfNull(branches);
        return new CiAiBranchPolicyInventory(true, branches, null);
    }

    public static CiAiBranchPolicyInventory Unavailable(string reason) =>
        new(false, Array.Empty<string>(), string.IsNullOrWhiteSpace(reason) ? "branch-inventory-unavailable" : reason);
}

/// <summary>Advisory branch-policy evaluation; never an authorization decision.</summary>
public sealed record CiAiBranchPolicyEvaluation
{
    internal CiAiBranchPolicyEvaluation(string profileName, CiAiBranchPolicyOutcome outcome,
        string reasonCode, IEnumerable<string> expected, IEnumerable<string> observed,
        string? configurationSourceCommitSha)
    {
        ProfileName = profileName;
        Outcome = outcome;
        ReasonCode = reasonCode;
        ExpectedBranches = Array.AsReadOnly(expected.ToArray());
        ObservedBranches = Array.AsReadOnly(observed.ToArray());
        ConfigurationSourceCommitSha = configurationSourceCommitSha;
    }

    public string ProfileName { get; }
    public CiAiBranchPolicyOutcome Outcome { get; }
    public string ReasonCode { get; }
    public IReadOnlyList<string> ExpectedBranches { get; }
    public IReadOnlyList<string> ObservedBranches { get; }
    public string? ConfigurationSourceCommitSha { get; }
    public bool GrantsWriteAuthority => false;
    public bool GrantsReleaseAuthority => false;
    public bool GrantsDeploymentAuthority => false;
}

/// <summary>
/// Resolves an exact-target, trusted-host-supplied profile and evaluates a complete branch inventory.
/// Missing configuration defaults to Sith. Invalid or stale configuration is unknown/invalid, never silently ignored.
/// </summary>
public static class CiAiBranchPolicyResolver
{
    public static CiAiBranchPolicyEvaluation Resolve(string repository, string targetCommitSha,
        CiAiBranchPolicyConfiguration? configuration, CiAiBranchPolicyInventory? inventory)
    {
        if (!CiAiBranchPolicyConfiguration.IsRepositoryIdentity(repository) ||
            !CiAiBranchPolicyConfiguration.IsFullSha(targetCommitSha))
            return Result("Unknown", CiAiBranchPolicyOutcome.InvalidConfiguration,
                "branch-policy.target-identity-invalid", Array.Empty<string>(), inventory, null);

        var normalizedRepository = repository;
        var normalizedSha = targetCommitSha.ToLowerInvariant();
        var profile = CiAiBranchPolicyProfile.SithPolicy();
        string? sourceSha = null;

        if (configuration is not null)
        {
            if (!string.Equals(configuration.Repository, normalizedRepository, StringComparison.OrdinalIgnoreCase))
                return Result("InvalidConfiguration", CiAiBranchPolicyOutcome.InvalidConfiguration,
                    "branch-policy.repository-mismatch", Array.Empty<string>(), inventory, configuration.ConfigurationSourceCommitSha);
            if (!string.Equals(configuration.TargetCommitSha, normalizedSha, StringComparison.Ordinal))
                return Result("InvalidConfiguration", CiAiBranchPolicyOutcome.InvalidConfiguration,
                    "branch-policy.target-sha-mismatch", Array.Empty<string>(), inventory, configuration.ConfigurationSourceCommitSha);
            profile = configuration.Profile;
            sourceSha = configuration.ConfigurationSourceCommitSha;
        }

        if (profile.Kind == CiAiBranchPolicyKind.Disabled)
            return Result(profile.Name, CiAiBranchPolicyOutcome.NotApplicable,
                "branch-policy.explicitly-disabled", Array.Empty<string>(), inventory, sourceSha);

        var expected = profile.Kind == CiAiBranchPolicyKind.Sith
            ? new[] { "development", "master" }
            : profile.ExpectedBranches.ToArray();

        if (inventory is null || !inventory.IsComplete)
            return Result(profile.Name, CiAiBranchPolicyOutcome.Unknown,
                inventory?.FailureReason ?? "branch-policy.inventory-missing", expected, inventory, sourceSha);

        var matches = expected.Length == inventory.Branches.Count &&
            expected.All(branch => inventory.Branches.Contains(branch, StringComparer.Ordinal));
        return Result(profile.Name, matches ? CiAiBranchPolicyOutcome.Compliant : CiAiBranchPolicyOutcome.Deviation,
            matches ? "branch-policy.inventory-matches-profile" : "branch-policy.inventory-differs-from-profile",
            expected, inventory, sourceSha);
    }

    private static CiAiBranchPolicyEvaluation Result(string name, CiAiBranchPolicyOutcome outcome,
        string reason, IEnumerable<string> expected, CiAiBranchPolicyInventory? inventory, string? sourceSha) =>
        new(name, outcome, reason, expected, inventory?.Branches ?? Array.Empty<string>(), sourceSha);
}
