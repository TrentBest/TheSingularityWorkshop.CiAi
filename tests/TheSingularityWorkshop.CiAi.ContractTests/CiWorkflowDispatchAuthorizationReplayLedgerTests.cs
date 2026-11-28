using TheSingularityWorkshop.CiAi;

internal static class CiWorkflowDispatchAuthorizationReplayLedgerTests
{
    private const string Authority = "trusted-human:trent";
    private const string RequestDigest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string WorkflowSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string CandidateSha = "cccccccccccccccccccccccccccccccccccccccc";

    public static int Run()
    {
        var passed = 0;
        var now = new DateTimeOffset(2026, 11, 28, 2, 0, 0, TimeSpan.Zero);

        Check("file ledger atomically consumes an authorization ID once and persists it", () =>
        {
            WithLedger((path, ledger) =>
            {
                var (_, authorization, _) = CreateValid(now);
                Assert(ledger.TryConsume(authorization, now), "first consume should be recorded");
                Assert(!ledger.TryConsume(authorization, now), "second consume must be rejected");
                Assert(ledger.ReadConsumedAuthorizationIds().Contains(authorization.AuthorizationId),
                    "consumed authorization ID was not retained");
                Assert(new FileCiWorkflowDispatchAuthorizationReplayLedger(path)
                    .ReadConsumedAuthorizationIds().Contains(authorization.AuthorizationId),
                    "a new ledger instance did not observe the durable consumed ID");
            });
        });

        Check("ledger retains complete authorization facts", () =>
        {
            WithLedger((path, ledger) =>
            {
                var (_, authorization, _) = CreateValid(now);
                Assert(ledger.TryConsume(authorization, now), "authorization was not consumed");
                var serialized = File.ReadAllText(path);
                Assert(serialized.Contains(authorization.AuthorityIdentity, StringComparison.Ordinal) &&
                    serialized.Contains(authorization.HostObservationDigest, StringComparison.Ordinal) &&
                    serialized.Contains(authorization.CandidateSha, StringComparison.Ordinal),
                    "ledger omitted authority, observation, or candidate provenance");
            });
        });

        Check("modified ledger record fails closed", () =>
        {
            WithLedger((path, ledger) =>
            {
                var (_, authorization, _) = CreateValid(now);
                ledger.TryConsume(authorization, now);
                var content = File.ReadAllText(path).Replace(RequestDigest, OtherDigest(), StringComparison.Ordinal);
                File.WriteAllText(path, content);
                AssertThrows<InvalidDataException>(() => ledger.ReadConsumedAuthorizationIds(),
                    "modified ledger record was accepted");
            });
        });

        Check("truncated ledger record fails closed", () =>
        {
            WithLedger((path, ledger) =>
            {
                var (_, authorization, _) = CreateValid(now);
                ledger.TryConsume(authorization, now);
                File.WriteAllText(path, File.ReadAllText(path).TrimEnd('\n'));
                AssertThrows<InvalidDataException>(() => ledger.ReadConsumedAuthorizationIds(),
                    "truncated ledger record was accepted");
            });
        });

        Check("reordered ledger records fail the hash-chain check", () =>
        {
            WithLedger((path, ledger) =>
            {
                var (_, first, _) = CreateValid(now);
                var second = first with { AuthorizationId = "authorization-002" };
                Assert(ledger.TryConsume(first, now), "first record was not consumed");
                Assert(ledger.TryConsume(second, now.AddSeconds(1)), "second record was not consumed");
                var lines = File.ReadAllLines(path);
                File.WriteAllLines(path, lines.Reverse());
                AssertThrows<InvalidDataException>(() => ledger.ReadConsumedAuthorizationIds(),
                    "reordered hash-chain records were accepted");
            });
        });

        Check("duplicate authorization IDs in a tampered ledger fail closed", () =>
        {
            WithLedger((path, ledger) =>
            {
                var (_, authorization, _) = CreateValid(now);
                ledger.TryConsume(authorization, now);
                var firstLine = File.ReadAllLines(path).Single();
                File.AppendAllText(path, firstLine + "\n");
                AssertThrows<InvalidDataException>(() => ledger.ReadConsumedAuthorizationIds(),
                    "duplicate authorization ID record was accepted");
            });
        });

        Check("invalid authorization cannot be consumed", () =>
        {
            WithLedger((path, ledger) =>
            {
                var (_, authorization, _) = CreateValid(now);
                var invalid = authorization with { HostObservationDigest = "bad" };
                AssertThrows<ArgumentException>(() => ledger.TryConsume(invalid, now),
                    "invalid authorization digest was consumed");
                Assert(ledger.ReadConsumedAuthorizationIds().Count == 0,
                    "invalid authorization left a ledger record");
            });
        });

        Check("dry-run planner returns exact inputs without consuming authorization", () =>
        {
            WithLedger((path, ledger) =>
            {
                var (subject, authorization, observation) = CreateValid(now);
                var result = CiWorkflowDispatchDryRunPlanner.Plan(subject, authorization, observation,
                    Authority, new HashSet<string>(StringComparer.Ordinal) { Authority }, ledger, now);
                Assert(result.IsReady && result.OutcomeCode == "dry-run-ready", "valid dry-run plan was denied");
                Assert(result.Plan is not null &&
                    result.Plan.WorkflowDispatchRef == "refs/heads/development" &&
                    result.Plan.WorkflowDefinitionSha == WorkflowSha &&
                    result.Plan.CandidateRef == subject.CandidateRef &&
                    result.Plan.CandidateSha == CandidateSha &&
                    result.Plan.SourceRunId == subject.SourceRunId,
                    "dry-run plan did not preserve exact refs, SHAs, and source run identity");
                Assert(result.Summary!.Contains("No GitHub API call was made", StringComparison.Ordinal),
                    "dry-run summary did not state that no dispatch occurred");
                Assert(ledger.ReadConsumedAuthorizationIds().Count == 0,
                    "dry-run planner consumed the authorization");
            });
        });

        Check("dry-run planner denies an already-consumed authorization", () =>
        {
            WithLedger((path, ledger) =>
            {
                var (subject, authorization, observation) = CreateValid(now);
                ledger.TryConsume(authorization, now);
                var result = CiWorkflowDispatchDryRunPlanner.Plan(subject, authorization, observation,
                    Authority, new HashSet<string>(StringComparer.Ordinal) { Authority }, ledger, now);
                Assert(!result.IsReady && result.DenialCodes.Contains("dispatch.authorization-replayed"),
                    "dry-run planner did not detect replay");
            });
        });

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS durable dispatch replay ledger {passed}: {name}");
        }
    }

    private static void WithLedger(Action<string, FileCiWorkflowDispatchAuthorizationReplayLedger> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "CiAiDispatchLedgerTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "dispatch-replay.jsonl");
        try
        {
            action(path, new FileCiWorkflowDispatchAuthorizationReplayLedger(path));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static (CiWorkflowDispatchAuthorizationSubject Subject,
        CiWorkflowDispatchAuthorization Authorization,
        CiWorkflowDispatchHostObservation Observation) CreateValid(DateTimeOffset now)
    {
        var subject = new CiWorkflowDispatchAuthorizationSubject(
            "rerun-request-001", RequestDigest, "TrentBest/raWWar",
            ".github/workflows/ci.yml", "refs/heads/development", WorkflowSha,
            "refs/heads/integration/autonomous-development-pipeline", CandidateSha,
            12345, 1, 2, now.AddMinutes(-3), now.AddMinutes(10));

        var observation = new CiWorkflowDispatchHostObservation(
            subject.Repository, subject.WorkflowPath, subject.WorkflowDefinitionRef,
            subject.WorkflowDefinitionSha, subject.CandidateRef, subject.CandidateSha,
            subject.SourceRunId, subject.SourceRunAttempt, true, now.AddSeconds(-3));

        var authorization = new CiWorkflowDispatchAuthorization(
            CiWorkflowDispatchAuthorizationEvaluator.ExpectedAuthorizationSchema,
            "authorization-001", Authority, subject.RequestId, subject.RequestDigest,
            subject.Repository, subject.WorkflowPath, subject.WorkflowDefinitionRef,
            subject.WorkflowDefinitionSha, subject.CandidateRef, subject.CandidateSha,
            subject.SourceRunId, subject.SourceRunAttempt, 1,
            now.AddSeconds(-2), now.AddMinutes(5), observation.ComputeDigest());

        return (subject, authorization, observation);
    }

    private static string OtherDigest() =>
        "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";

    private static void AssertThrows<TException>(Action action, string message) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
