using System.Security.Cryptography;
using System.Text;

namespace TheSingularityWorkshop.CiAi;

/// <summary>How the trusted repository adapter observed the root onboarding file at one commit.</summary>
public enum AgentOnboardingReadStatus
{
    Available,
    Missing,
    Unreadable,
    Unknown
}

/// <summary>
/// Repository content observation supplied by a host adapter. The adapter must retrieve this exact
/// path at RequestedCommitSha and report the commit actually used for the read.
/// </summary>
public sealed record AgentOnboardingSourceObservation(
    string Repository,
    string RequestedCommitSha,
    string? ResolvedCommitSha,
    string Path,
    AgentOnboardingReadStatus ReadStatus,
    string? Content);

/// <summary>Deterministic, auditable result of the mandatory ADP onboarding check.</summary>
public sealed record AgentOnboardingAdmissionResult(
    string Repository,
    string RequestedCommitSha,
    string Path,
    string ContractVersion,
    bool IsEligible,
    string Outcome,
    string? FailureCode,
    string Reason,
    string? ContentSha256);

/// <summary>
/// Validates the root-level AgentOnboarding.md contract for one exact repository commit.
/// This validates repository data; it does not grant permissions or authorize dispatch.
/// </summary>
public static class AgentOnboardingValidator
{
    public const string ContractVersion = "ciai.agent-onboarding.v1";
    public const string RequiredPath = "AgentOnboarding.md";
    public const int MaximumContentBytes = 64 * 1024;

    private static readonly string[] RequiredHeadings =
    [
        "# Agent Onboarding",
        "## Why this file exists",
        "## Mandatory admission rule",
        "## First-pass agent procedure",
        "## Safety and authority boundaries",
        "## Repository-specific instructions",
        "## Completion report"
    ];

    public static AgentOnboardingAdmissionResult Evaluate(AgentOnboardingSourceObservation source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var repository = source.Repository ?? string.Empty;
        var requestedSha = source.RequestedCommitSha ?? string.Empty;
        var path = source.Path ?? string.Empty;

        if (!RepositoryIdentity.IsValid(repository))
            return Reject(repository, requestedSha, path, "repository.invalid",
                "A canonical owner/repository identity is required.");

        if (!IsFullSha(requestedSha))
            return Reject(repository, requestedSha, path, "target-sha.invalid",
                "The requested target must be a full 40-character hexadecimal commit SHA.");

        if (!string.Equals(path, RequiredPath, StringComparison.Ordinal))
            return Reject(repository, requestedSha, path, "onboarding.path-mismatch",
                "Only the root-level AgentOnboarding.md path is accepted; no fallback path is permitted.");

        if (!Enum.IsDefined(source.ReadStatus))
            return Reject(repository, requestedSha, path, "onboarding.read-status-unknown",
                "The repository adapter returned an unknown read status.");

        if (source.ReadStatus == AgentOnboardingReadStatus.Missing)
            return Reject(repository, requestedSha, path, "onboarding.missing",
                "The exact target commit does not contain the required root-level onboarding file.");

        if (source.ReadStatus == AgentOnboardingReadStatus.Unreadable)
            return Reject(repository, requestedSha, path, "onboarding.unreadable",
                "The required onboarding file could not be read from the exact target commit.");

        if (source.ReadStatus != AgentOnboardingReadStatus.Available)
            return Reject(repository, requestedSha, path, "onboarding.read-status-unknown",
                "The required onboarding file was not positively observed.");

        if (!IsFullSha(source.ResolvedCommitSha))
            return Reject(repository, requestedSha, path, "onboarding.resolved-sha-invalid",
                "The repository adapter did not identify a full commit SHA for the file read.");

        if (!string.Equals(requestedSha, source.ResolvedCommitSha, StringComparison.OrdinalIgnoreCase))
            return Reject(repository, requestedSha, path, "onboarding.target-sha-mismatch",
                "The onboarding content was not retrieved from the exact requested target commit.");

        if (source.Content is null || string.IsNullOrWhiteSpace(source.Content))
            return Reject(repository, requestedSha, path, "onboarding.empty",
                "The exact target commit contains an empty onboarding document.");

        byte[] bytes;
        try
        {
            bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetBytes(source.Content);
        }
        catch (EncoderFallbackException)
        {
            return Reject(repository, requestedSha, path, "onboarding.malformed-utf8",
                "The onboarding document contains invalid UTF-8 text.");
        }

        if (bytes.Length > MaximumContentBytes)
            return Reject(repository, requestedSha, path, "onboarding.exceeds-size-limit",
                $"The onboarding document exceeds the {MaximumContentBytes}-byte limit.");

        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var headings = source.Content.Split('\n')
            .Select(line => line.Trim().TrimEnd('\r'))
            .ToHashSet(StringComparer.Ordinal);
        var missingHeadings = RequiredHeadings.Where(heading => !headings.Contains(heading)).ToArray();
        if (missingHeadings.Length > 0)
            return Reject(repository, requestedSha, path, "onboarding.malformed",
                $"Required section(s) are missing: {string.Join(", ", missingHeadings)}.", digest);

        var requiredStatements = new[]
        {
            "MUST contain a root-level `AgentOnboarding.md`",
            "exact target commit",
            "untrusted repository data",
            "human approval"
        };
        var missingStatements = requiredStatements
            .Where(statement => !source.Content.Contains(statement, StringComparison.Ordinal))
            .ToArray();
        if (missingStatements.Length > 0)
            return Reject(repository, requestedSha, path, "onboarding.contract-incomplete",
                $"Required admission statement(s) are missing: {string.Join(", ", missingStatements)}.", digest);

        return new AgentOnboardingAdmissionResult(repository, requestedSha, RequiredPath,
            ContractVersion, true, "ELIGIBLE", null,
            "The root onboarding document satisfies the CiAi v1 contract at the exact requested commit. This result is necessary but not sufficient for autonomous work.",
            digest);
    }

    private static AgentOnboardingAdmissionResult Reject(
        string repository, string requestedSha, string path, string failureCode,
        string reason, string? digest = null) =>
        new(repository, requestedSha, path, ContractVersion, false, "NOT_ELIGIBLE",
            failureCode, reason, digest);

    private static bool IsFullSha(string? value) =>
        value is { Length: 40 } &&
        value.All(character => char.IsAsciiHexDigit(character));
}
