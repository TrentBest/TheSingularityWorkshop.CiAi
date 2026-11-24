using TheSingularityWorkshop.CiAi;

internal static class CiPublicationPolicyContractTests
{
    public static int Run()
    {
        var passed = 0;
        var complete = new CiPublicationEvidence("TrentBest/Example", "development",
            "abcdef0123456789abcdef0123456789abcdef01", 42, 1, true, true, true, true);

        Check("Pages automatic policy allows successful exact evidence without human approval", () =>
        {
            var policy = new CiPublicationPolicy("github-pages", CiPublicationMode.Automatic, CiPublicationApproval.None);
            Assert(policy.Validate().Count == 0, "valid automatic Pages policy was rejected");
            Assert(policy.Evaluate(complete) == CiPublicationDecision.PublishAutomatically,
                "automatic Pages policy did not produce automatic eligibility");
        });
        Check("destination policy can require approval without changing other destinations", () =>
        {
            var policy = new CiPublicationPolicy("nuget", CiPublicationMode.Automatic, CiPublicationApproval.Required);
            Assert(policy.Evaluate(complete) == CiPublicationDecision.AwaitingApproval,
                "approval-required destination bypassed its gate");
        });
        Check("manual publication remains manual after successful evidence", () =>
        {
            var policy = new CiPublicationPolicy("external-host", CiPublicationMode.Manual, CiPublicationApproval.None);
            Assert(policy.Evaluate(complete) == CiPublicationDecision.AwaitingManualAction,
                "manual destination was treated as automatic");
        });
        Check("disabled destination cannot be enabled by successful evidence", () =>
        {
            var policy = new CiPublicationPolicy("disabled", CiPublicationMode.Disabled, CiPublicationApproval.None);
            Assert(policy.Evaluate(complete) == CiPublicationDecision.Disabled,
                "disabled destination became eligible");
        });
        Check("failed build blocks publication", () =>
        {
            var policy = new CiPublicationPolicy("github-pages", CiPublicationMode.Automatic, CiPublicationApproval.None);
            Assert(policy.Evaluate(complete with { BuildSucceeded = false }) == CiPublicationDecision.Blocked,
                "failed build was eligible");
        });
        Check("incomplete, stale, and ambiguous evidence each block publication", () =>
        {
            var policy = new CiPublicationPolicy("github-pages", CiPublicationMode.Automatic, CiPublicationApproval.None);
            Assert(policy.Evaluate(complete with { EvidenceComplete = false }) == CiPublicationDecision.Blocked,
                "incomplete evidence was eligible");
            Assert(policy.Evaluate(complete with { IsCurrentAttempt = false }) == CiPublicationDecision.Blocked,
                "stale attempt was eligible");
            Assert(policy.Evaluate(complete with { IdentityIsUnambiguous = false }) == CiPublicationDecision.Blocked,
                "ambiguous attempt was eligible");
        });
        Check("invalid destination policy fails closed", () =>
        {
            var policy = new CiPublicationPolicy("bad destination!", (CiPublicationMode)99, CiPublicationApproval.None);
            Assert(policy.Evaluate(complete) == CiPublicationDecision.InvalidPolicy,
                "invalid policy was accepted");
        });
        Check("disabled policy cannot contain an approval requirement", () =>
        {
            var policy = new CiPublicationPolicy("disabled", CiPublicationMode.Disabled, CiPublicationApproval.Required);
            Assert(policy.Validate().Contains("publication.disabled-cannot-require-approval"),
                "contradictory disabled policy was not diagnosed");
        });

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
