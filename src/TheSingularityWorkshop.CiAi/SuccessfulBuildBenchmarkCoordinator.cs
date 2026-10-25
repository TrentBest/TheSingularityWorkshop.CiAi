namespace TheSingularityWorkshop.CiAi;

/// <summary>Outcome of reconciling a source-build observation before benchmark dispatch.</summary>
public sealed record BenchmarkDispatchDecision(
    bool Dispatched,
    string? NoticeId,
    IReadOnlyList<string> VerificationErrors);

/// <summary>
/// Fail-closed orchestration boundary: reconcile observed CI facts against the coordinator's
/// expected repository, run ID, and commit before sending a benchmark notice.
/// </summary>
public sealed class SuccessfulBuildBenchmarkCoordinator
{
    private readonly GitHubActionsBenchmarkDispatchClient _dispatchClient;
    private readonly string _benchmarkRepository;

    public SuccessfulBuildBenchmarkCoordinator(
        GitHubActionsBenchmarkDispatchClient dispatchClient,
        string benchmarkRepository)
    {
        _dispatchClient = dispatchClient ?? throw new ArgumentNullException(nameof(dispatchClient));
        ArgumentException.ThrowIfNullOrWhiteSpace(benchmarkRepository);
        if (!RepositoryIdentity.IsValid(benchmarkRepository))
            throw new ArgumentException("Benchmark repository must be canonical owner/repository.", nameof(benchmarkRepository));
        _benchmarkRepository = benchmarkRepository;
    }

    public async Task<BenchmarkDispatchDecision> DispatchVerifiedSuccessAsync(
        CiRunSnapshot? observedRun,
        string expectedSourceRepository,
        long expectedRunId,
        string expectedHeadSha,
        CancellationToken cancellationToken = default)
    {
        var errors = CiRunEvidenceVerifier.VerifySuccessfulRun(
            observedRun, expectedSourceRepository, expectedRunId, expectedHeadSha);
        if (errors.Count != 0)
            return new BenchmarkDispatchDecision(false, null, errors);

        var notice = new BenchmarkBuildNotice(observedRun!, _benchmarkRepository);
        await _dispatchClient.DispatchSuccessfulBuildAsync(notice, cancellationToken).ConfigureAwait(false);
        return new BenchmarkDispatchDecision(true, notice.NoticeId, Array.Empty<string>());
    }
}
