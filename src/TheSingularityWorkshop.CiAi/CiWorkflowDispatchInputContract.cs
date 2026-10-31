using System.Collections.ObjectModel;
using System.Globalization;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Maps one validated rerun request to the opt-in workflow_dispatch input contract.
/// This creates data only; it does not authenticate, resolve GitHub state, or dispatch a workflow.
/// </summary>
public static class CiWorkflowDispatchInputContract
{
    public const string TrustedWorkflowRef = "refs/heads/development";
    public const string TargetBranchInput = "target_branch";
    public const string RequestIdInput = "ciai_request_id";
    public const string CorrelationIdInput = "ciai_correlation_id";
    public const string TaskIdInput = "ciai_task_id";
    public const string CandidateShaInput = "ciai_candidate_sha";
    public const string AttemptNumberInput = "ciai_attempt_number";

    /// <summary>Creates the complete, immutable input set expected by CiAi's correlated workflow gate.</summary>
    public static IReadOnlyDictionary<string, string> CreateInputs(CiWorkflowRerunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CiWorkflowRerunRequest.IsFeatureBranchRef(request.HeadRef))
            throw new ArgumentException("Dispatch inputs require a valid isolated feature branch.", nameof(request));
        if (!IsWorkflowIdentity(request.RequestId) ||
            !IsWorkflowIdentity(request.CorrelationId) ||
            !IsWorkflowIdentity(request.TaskId))
            throw new ArgumentException("Request, correlation, and task IDs must match the workflow's bounded ASCII identity format.", nameof(request));

        var branchName = request.HeadRef["refs/heads/".Length..];
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TargetBranchInput] = branchName,
            [RequestIdInput] = request.RequestId,
            [CorrelationIdInput] = request.CorrelationId,
            [TaskIdInput] = request.TaskId,
            [CandidateShaInput] = request.HeadSha,
            [AttemptNumberInput] = request.AttemptNumber.ToString(CultureInfo.InvariantCulture)
        };
        return new ReadOnlyDictionary<string, string>(inputs);
    }

    internal static bool IsWorkflowIdentity(string value) =>
        !string.IsNullOrEmpty(value) &&
        value.Length <= 200 &&
        char.IsAsciiLetterOrDigit(value[0]) &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '.' or '_' or ':' or '-');
}
