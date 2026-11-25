using TheSingularityWorkshop.CiAi;

internal static class CiWorkflowRerunRequestLedgerTests
{
    private const string CandidateSha = "abcdef0123456789abcdef0123456789abcdef01";
    private const string OtherCandidateSha = "123456789abcdef0123456789abcdef012345678";
    private static readonly DateTimeOffset RequestedAt = DateTimeOffset.Parse("2026-11-24T20:00:00Z");

    public static int Run()
    {
        var passed = 0;
        var root = Path.Combine(Path.GetTempPath(), "ciai-rerun-ledger-" + Guid.NewGuid().ToString("N"));
        try
        {
            var request = CreateRequest();
            Check("dispatch input binds a canonical repository, workflow, feature ref, and exact SHA", () =>
            {
                Assert(request.Dispatch.Repository == "TrentBest/Experiences" &&
                    request.Dispatch.WorkflowPath == ".github/workflows/ci.yml" &&
                    request.Dispatch.CandidateRef == "refs/heads/feature/minesweeper-domain-pilot" &&
                    request.Dispatch.CandidateSha == CandidateSha,
                    "dispatch identity was not preserved");
            });

            Check("canonical and non-feature refs cannot be rerun", () =>
            {
                Throws<ArgumentException>(() => new CiWorkflowDispatchInputContract(
                    "TrentBest/Experiences", ".github/workflows/ci.yml", "refs/heads/development", CandidateSha));
                Throws<ArgumentException>(() => new CiWorkflowDispatchInputContract(
                    "TrentBest/Experiences", ".github/workflows/ci.yml", "refs/heads/master", CandidateSha));
                Throws<ArgumentException>(() => new CiWorkflowDispatchInputContract(
                    "TrentBest/Experiences", ".github/workflows/ci.yml", "refs/heads/work/agent", CandidateSha));
            });

            Check("workflow path rejects traversal and non-workflow locations", () =>
            {
                Throws<ArgumentException>(() => new CiWorkflowDispatchInputContract(
                    "TrentBest/Experiences", ".github/workflows/../ci.yml",
                    "refs/heads/feature/minesweeper-domain-pilot", CandidateSha));
                Throws<ArgumentException>(() => new CiWorkflowDispatchInputContract(
                    "TrentBest/Experiences", "src/ci.yml",
                    "refs/heads/feature/minesweeper-domain-pilot", CandidateSha));
            });

            Check("candidate SHA must be a full commit identity", () =>
            {
                Throws<ArgumentException>(() => new CiWorkflowDispatchInputContract(
                    "TrentBest/Experiences", ".github/workflows/ci.yml",
                    "refs/heads/feature/minesweeper-domain-pilot", "deadbeef"));
            });

            Check("source run, attempt, expiry, and retry budget are bounded", () =>
            {
                Throws<ArgumentOutOfRangeException>(() => CreateRequest(sourceRunId: 0));
                Throws<ArgumentOutOfRangeException>(() => CreateRequest(sourceRunAttempt: 0));
                Throws<ArgumentOutOfRangeException>(() => CreateRequest(maximumAdditionalAttempts: 0));
                Throws<ArgumentOutOfRangeException>(() => CreateRequest(maximumAdditionalAttempts: 4));
                Throws<ArgumentException>(() => CreateRequest(expiresAt: RequestedAt.AddDays(8)));
                Throws<ArgumentException>(() => CreateRequest(expiresAt: RequestedAt));
            });

            Check("request digest is deterministic and includes source attempt and candidate", () =>
            {
                var same = CreateRequest();
                var otherAttempt = CreateRequest(sourceRunAttempt: 2, requestId: "request-2",
                    idempotencyKey: "idempotency-2");
                var otherSha = CreateRequest(candidateSha: OtherCandidateSha, requestId: "request-3",
                    idempotencyKey: "idempotency-3");
                Assert(request.RequestDigest == same.RequestDigest &&
                    request.RequestDigest != otherAttempt.RequestDigest &&
                    request.RequestDigest != otherSha.RequestDigest,
                    "request digest did not bind immutable source identity");
            });

            Check("ledger appends, reloads, and enumerates immutable requests", () =>
            {
                var ledger = new FileCiWorkflowRerunRequestLedger(root);
                Assert(ledger.Enqueue(request) == CiWorkflowRerunRequestLedgerOutcome.Saved,
                    "first request was not appended");
                var reopened = new FileCiWorkflowRerunRequestLedger(root);
                var loaded = reopened.LoadByIdempotencyKey(request.IdempotencyKey);
                Assert(loaded is not null && loaded.RequestDigest == request.RequestDigest &&
                    loaded.Dispatch.CandidateSha == CandidateSha &&
                    loaded.SourceRunId == 41234078182 && loaded.SourceRunAttempt == 1 &&
                    loaded.CorrelationId == "task-minesweeper-001",
                    "reloaded request lost its exact correlation or source binding");
                Assert(reopened.ReadAll().Count == 1, "ledger did not return the durable request");
            });

            Check("identical idempotency retries do not create duplicate records", () =>
            {
                Assert(new FileCiWorkflowRerunRequestLedger(root).Enqueue(CreateRequest()) ==
                    CiWorkflowRerunRequestLedgerOutcome.AlreadyExists,
                    "identical retry was not idempotent");
                Assert(new FileCiWorkflowRerunRequestLedger(root).ReadAll().Count == 1,
                    "identical retry created another record");
            });

            Check("same idempotency key with changed content is a conflict", () =>
            {
                var changed = CreateRequest(candidateSha: OtherCandidateSha);
                Assert(new FileCiWorkflowRerunRequestLedger(root).Enqueue(changed) ==
                    CiWorkflowRerunRequestLedgerOutcome.Conflict,
                    "changed request reused an idempotency key");
            });

            Check("same request ID cannot be rebound to another idempotency key", () =>
            {
                var changed = CreateRequest(requestId: "request-1", idempotencyKey: "idempotency-other");
                Assert(new FileCiWorkflowRerunRequestLedger(root).Enqueue(changed) ==
                    CiWorkflowRerunRequestLedgerOutcome.Conflict,
                    "request ID was rebound");
            });

            Check("later run attempts remain separate append-only records", () =>
            {
                var later = CreateRequest(sourceRunAttempt: 2, requestId: "request-attempt-2",
                    idempotencyKey: "idempotency-attempt-2");
                var ledger = new FileCiWorkflowRerunRequestLedger(root);
                Assert(ledger.Enqueue(later) == CiWorkflowRerunRequestLedgerOutcome.Saved,
                    "later attempt request was not retained");
                var all = ledger.ReadAll();
                Assert(all.Count == 2 && all.Any(item => item.SourceRunAttempt == 1) &&
                    all.Any(item => item.SourceRunAttempt == 2),
                    "later attempt overwrote the original request");
            });

            Check("tampered ledger bytes are rejected by request digest verification", () =>
            {
                var file = Directory.EnumerateFiles(root, "*.json").First();
                var json = File.ReadAllText(file);
                File.WriteAllText(file, json.Replace(CandidateSha, OtherCandidateSha, StringComparison.Ordinal));
                Throws<InvalidDataException>(() => new FileCiWorkflowRerunRequestLedger(root).ReadAll());
            });
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS rerun request ledger {passed}: {name}");
        }
    }

    private static CiWorkflowRerunRequest CreateRequest(
        string requestId = "request-1",
        string idempotencyKey = "idempotency-1",
        string candidateSha = CandidateSha,
        long sourceRunId = 41234078182,
        int sourceRunAttempt = 1,
        int maximumAdditionalAttempts = 2,
        DateTimeOffset? expiresAt = null) =>
        new(requestId, idempotencyKey, "task-minesweeper-001",
            new CiWorkflowDispatchInputContract("TrentBest/Experiences",
                ".github/workflows/ci.yml",
                "refs/heads/feature/minesweeper-domain-pilot",
                candidateSha),
            sourceRunId, sourceRunAttempt, maximumAdditionalAttempts,
            RequestedAt, expiresAt ?? RequestedAt.AddDays(1));

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
