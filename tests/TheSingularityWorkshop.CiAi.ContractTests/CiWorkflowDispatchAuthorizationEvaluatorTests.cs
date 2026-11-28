using TheSingularityWorkshop.CiAi;

internal static class CiWorkflowDispatchAuthorizationEvaluatorTests
{
    private const string Authority = "trusted-human:trent";
    private const string RequestDigest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string WorkflowSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string CandidateSha = "cccccccccccccccccccccccccccccccccccccccc";
    private const string OtherSha = "dddddddddddddddddddddddddddddddddddddddd";

    public static int Run()
    {
        var passed = 0;
        var now = new DateTimeOffset(2026, 11, 28, 2, 0, 0, TimeSpan.Zero);

        Check("fresh trusted authorization bound to exact request and live SHAs is accepted", () =>
        {
            var (subject, authorization, observation) = CreateValid(now);
            var decision = Evaluate(subject, authorization, observation, now);
            Assert(decision.IsAuthorized && decision.OutcomeCode == "authorized", "valid host authorization was denied");
        });

        Check("unsupported authorization schema is denied", () =>
        {
            var (subject, authorization, observation) = CreateValid(now);
            var unsupported = authorization with { SchemaVersion = "ciai.workflow-dispatch-authorization.v2" };
            var decision = Evaluate(subject, unsupported, observation, now);
            AssertDenied(decision, "dispatch.authorization-schema-unsupported");
        });

        Check("untrusted or unauthenticated authority is denied", () =>
        {
            var (subject, authorization, observation) = CreateValid(now);
            var decision = Evaluate(subject, authorization, observation, now, authenticatedAuthority: "untrusted-agent");
            AssertDenied(decision, "dispatch.authority-not-trusted");
        });

        Check("request digest substitution is denied", () =>
        {
            var (subject, authorization, observation) = CreateValid(now);
            var decision = Evaluate(subject, authorization with { RequestDigest = OtherDigest() }, observation, now);
            AssertDenied(decision, "dispatch.request-digest-mismatch");
        });

        Check("candidate ref SHA movement is denied even with a fresh matching observation digest", () =>
        {
            var (subject, authorization, observation) = CreateValid(now);
            var moved = observation with { CandidateSha = OtherSha };
            var updatedAuthorization = authorization with { HostObservationDigest = moved.ComputeDigest() };
            var decision = Evaluate(subject, updatedAuthorization, moved, now);
            AssertDenied(decision, "dispatch.observed-candidate-sha-mismatch");
        });

        Check("workflow definition SHA movement is denied", () =>
        {
            var (subject, authorization, observation) = CreateValid(now);
            var moved = observation with { WorkflowDefinitionSha = OtherSha };
            var updatedAuthorization = authorization with { HostObservationDigest = moved.ComputeDigest() };
            var decision = Evaluate(subject, updatedAuthorization, moved, now);
            AssertDenied(decision, "dispatch.observed-workflow-sha-mismatch");
        });

        Check("workflow without workflow_dispatch is denied", () =>
        {
            var (subject, authorization, observation) = CreateValid(now);
            var disabled = observation with { WorkflowDispatchEnabled = false };
            var updatedAuthorization = authorization with { HostObservationDigest = disabled.ComputeDigest() };
            var decision = Evaluate(subject, updatedAuthorization, disabled, now);
            AssertDenied(decision, "dispatch.workflow-dispatch-not-enabled");
        });

        Check("stale host observation is denied", () =>
        {
            var (subject, authorization, observation) = CreateValid(now);
            var stale = observation with { ObservedAtUtc = now.AddSeconds(-61) };
            var updatedAuthorization = authorization with { HostObservationDigest = stale.ComputeDigest() };
            var decision = Evaluate(subject, updatedAuthorization, stale, now);
            AssertDenied(decision, "dispatch.observation-stale");
        });

        Check("future-dated host observation is denied", () =>
        {
            var (subject, authorization, observation) = CreateValid(now);
            var future = observation with { ObservedAtUtc = now.AddSeconds(1) };
            var updatedAuthorization = authorization with { HostObservationDigest = future.ComputeDigest() };
            var decision = Evaluate(subject, updatedAuthorization, future, now);
            AssertDenied(decision, "dispatch.observation-from-future");
        });

        Check("expired authorization is denied", () =>
        {
            var (subject, authorization, observation) = CreateValid(now);
            var expired = authorization with { ExpiresAtUtc = now.AddSeconds(-1) };
            var decision = Evaluate(subject, expired, observation, now);
            AssertDenied(decision, "dispatch.authorization-expired");
        });

        Check("authorization lifetime above fifteen minutes is denied", () =>
        {
            var (subject, authorization, observation) = CreateValid(now);
            var longLived = authorization with { ExpiresAtUtc = now.AddMinutes(16) };
            var decision = Evaluate(subject, longLived, observation, now);
            AssertDenied(decision, "dispatch.authorization-lifetime-invalid");
        });

        Check("additional attempt outside request budget is denied", () =>
        {
            var (subject, authorization, observation) = CreateValid(now);
            var excessive = authorization with { AdditionalAttemptNumber = 3 };
            var decision = Evaluate(subject, excessive, observation, now);
            AssertDenied(decision, "dispatch.additional-attempt-out-of-budget");
        });

        Check("consumed authorization ID is rejected as replay", () =>
        {
            var (subject, authorization, observation) = CreateValid(now);
            var decision = Evaluate(subject, authorization, observation, now,
                consumedIds: new HashSet<string>(StringComparer.Ordinal) { authorization.AuthorizationId });
            AssertDenied(decision, "dispatch.authorization-replayed");
        });

        Check("tampered observation digest is denied", () =>
        {
            var (subject, authorization, observation) = CreateValid(now);
            var tampered = authorization with { HostObservationDigest = OtherDigest() };
            var decision = Evaluate(subject, tampered, observation, now);
            AssertDenied(decision, "dispatch.host-observation-digest-mismatch");
        });

        Check("malformed null observation SHA fails closed without throwing", () =>
        {
            var (subject, authorization, observation) = CreateValid(now);
            var malformed = observation with { CandidateSha = null! };
            var updatedAuthorization = authorization with { HostObservationDigest = malformed.ComputeDigest() };
            var decision = Evaluate(subject, updatedAuthorization, malformed, now);
            AssertDenied(decision, "dispatch.observation-sha-invalid");
        });

        Check("missing authorization ID fails closed without replay-ledger exceptions", () =>
        {
            var (subject, authorization, observation) = CreateValid(now);
            var malformed = authorization with { AuthorizationId = null! };
            var decision = Evaluate(subject, malformed, observation, now);
            AssertDenied(decision, "dispatch.authorization-id-invalid");
        });

        Check("source run identity substitution is denied", () =>
        {
            var (subject, authorization, observation) = CreateValid(now);
            var substituted = authorization with { SourceRunId = 99999 };
            var decision = Evaluate(subject, substituted, observation, now);
            AssertDenied(decision, "dispatch.source-run-mismatch");
        });

        Check("protected candidate branch is denied", () =>
        {
            var (subject, authorization, observation) = CreateValid(now);
            var protectedSubject = subject with { CandidateRef = "refs/heads/master" };
            var decision = Evaluate(protectedSubject, authorization, observation, now);
            AssertDenied(decision, "dispatch.subject-candidate-ref-invalid");
        });

        Check("workflow definition must be pinned to development", () =>
        {
            var (subject, authorization, observation) = CreateValid(now);
            var untrustedWorkflowRef = subject with { WorkflowDefinitionRef = "refs/heads/master" };
            var decision = Evaluate(untrustedWorkflowRef, authorization, observation, now);
            AssertDenied(decision, "dispatch.subject-workflow-ref-not-development");
        });

        Check("authorization cannot outlive its request", () =>
        {
            var (subject, authorization, observation) = CreateValid(now);
            var shortRequest = subject with { RequestExpiresAtUtc = now.AddMinutes(3) };
            var decision = Evaluate(shortRequest, authorization, observation, now);
            AssertDenied(decision, "dispatch.authorization-outlives-request");
        });

        Check("authorization ID is not reusable across a second evaluation", () =>
        {
            var (subject, authorization, observation) = CreateValid(now);
            var first = Evaluate(subject, authorization, observation, now);
            var second = Evaluate(subject, authorization, observation, now,
                consumedIds: new HashSet<string>(StringComparer.Ordinal) { authorization.AuthorizationId });
            Assert(first.IsAuthorized, "first authorization attempt should pass");
            AssertDenied(second, "dispatch.authorization-replayed");
        });

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS host dispatch authorization {passed}: {name}");
        }
    }

    private static (CiWorkflowDispatchAuthorizationSubject Subject,
        CiWorkflowDispatchAuthorization Authorization,
        CiWorkflowDispatchHostObservation Observation) CreateValid(DateTimeOffset now)
    {
        var subject = new CiWorkflowDispatchAuthorizationSubject(
            "rerun-request-001",
            RequestDigest,
            "TrentBest/raWWar",
            ".github/workflows/ci.yml",
            "refs/heads/development",
            WorkflowSha,
            "refs/heads/integration/autonomous-development-pipeline",
            CandidateSha,
            12345,
            1,
            2,
            now.AddMinutes(-3),
            now.AddMinutes(10));

        var observation = new CiWorkflowDispatchHostObservation(
            subject.Repository,
            subject.WorkflowPath,
            subject.WorkflowDefinitionRef,
            subject.WorkflowDefinitionSha,
            subject.CandidateRef,
            subject.CandidateSha,
            subject.SourceRunId,
            subject.SourceRunAttempt,
            true,
            now.AddSeconds(-3));

        var authorization = new CiWorkflowDispatchAuthorization(
            CiWorkflowDispatchAuthorizationEvaluator.ExpectedAuthorizationSchema,
            "authorization-001",
            Authority,
            subject.RequestId,
            subject.RequestDigest,
            subject.Repository,
            subject.WorkflowPath,
            subject.WorkflowDefinitionRef,
            subject.WorkflowDefinitionSha,
            subject.CandidateRef,
            subject.CandidateSha,
            subject.SourceRunId,
            subject.SourceRunAttempt,
            1,
            now.AddSeconds(-2),
            now.AddMinutes(5),
            observation.ComputeDigest());

        return (subject, authorization, observation);
    }

    private static CiWorkflowDispatchAuthorizationDecision Evaluate(
        CiWorkflowDispatchAuthorizationSubject subject,
        CiWorkflowDispatchAuthorization authorization,
        CiWorkflowDispatchHostObservation observation,
        DateTimeOffset now,
        string authenticatedAuthority = Authority,
        IReadOnlySet<string>? consumedIds = null) =>
        CiWorkflowDispatchAuthorizationEvaluator.Evaluate(
            subject,
            authorization,
            observation,
            authenticatedAuthority,
            new HashSet<string>(StringComparer.Ordinal) { Authority },
            consumedIds ?? new HashSet<string>(StringComparer.Ordinal),
            now);

    private static string OtherDigest() =>
        "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";

    private static void AssertDenied(CiWorkflowDispatchAuthorizationDecision decision, string expectedCode)
    {
        if (decision.IsAuthorized || !decision.DenialCodes.Contains(expectedCode))
            throw new InvalidOperationException($"Expected denial '{expectedCode}', got '{decision.OutcomeCode}'.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
