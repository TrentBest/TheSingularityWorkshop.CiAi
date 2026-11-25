using TheSingularityWorkshop.CiAi;

internal static class CiAiRepositoryAdmissionTests
{
    public static int Run()
    {
        var passed = 0;
        var now = DateTimeOffset.Parse("2026-11-25T20:00:00Z");
        var entitlement = new CiAiRepositoryEntitlement("licensee-opaque-1", "Community", 5,
            now.AddDays(-1), now.AddDays(30), CiAiEntitlementVerificationStatus.Verified);
        var upgrade = new Uri("https://trentbest.github.io/TheSingularityWorkshop.CiAi/#licensing");
        var four = Enumerable.Range(1, 4).Select(i => $"Owner/Repo{i}").ToArray();
        var five = Enumerable.Range(1, 5).Select(i => $"Owner/Repo{i}").ToArray();

        Check("free allowance admits first configured repository", () =>
            Assert(Evaluate("Owner/Repo1", Array.Empty<string>(), entitlement, now).MayStartWork,
                "first repository was not admitted"));
        Check("four configured repositories can add the fifth", () =>
            Assert(Evaluate("Owner/Repo5", four, entitlement, now).Outcome == CiAiRepositoryAdmissionOutcome.Admitted,
                "fifth repository was not admitted"));
        Check("five configured repositories reject the sixth with upgrade report", () =>
        {
            var report = Evaluate("Owner/Repo6", five, entitlement, now, upgrade);
            Assert(report.Outcome == CiAiRepositoryAdmissionOutcome.UpgradeRequired, "sixth repository was not rejected");
            Assert(report.ResultCode == CiAiRepositoryAdmissionEvaluator.RepositoryLimitExceededCode,
                "stable limit result code changed");
            Assert(report.ConfiguredRepositoryCount == 5 && report.MaximumAuthorizedRepositories == 5,
                "report omitted exact count or entitlement");
            Assert(report.NoWorkStarted && !report.MayStartWork, "rejected repository could start work");
            Assert(report.UpgradeUrl == upgrade, "trusted upgrade URL was not preserved");
            Assert(report.Message.Contains("No CiAi work was started", StringComparison.Ordinal),
                "report did not explain that work never started");
        });
        Check("re-adding existing repository is idempotent even at the limit", () =>
        {
            var report = Evaluate("owner/repo1", five, entitlement, now);
            Assert(report.Outcome == CiAiRepositoryAdmissionOutcome.Admitted &&
                report.ConfiguredRepositoryCount == 5 && report.MayStartWork,
                "existing repository re-add consumed a slot or was rejected");
        });
        Check("repository identities are de-duplicated case-insensitively", () =>
        {
            var report = Evaluate("Owner/New", new[] { "Owner/Repo1", "owner/repo1", "OWNER/REPO1" },
                entitlement, now);
            Assert(report.ConfiguredRepositoryCount == 2 && report.MayStartWork,
                "case variants consumed multiple slots");
        });
        Check("public and private repositories share the same identity count", () =>
        {
            var report = Evaluate("Owner/New", new[] { "Owner/Public", "Owner/Private" }, entitlement, now);
            Assert(report.ConfiguredRepositoryCount == 3, "repository visibility incorrectly changed counting");
        });
        Check("a configured fork is counted as a distinct repository identity", () =>
        {
            var report = Evaluate("Owner/Upstream", new[] { "Owner/Upstream-Fork1", "Owner/Upstream-Fork2",
                "Owner/Other1", "Owner/Other2", "Owner/Other3" }, entitlement, now);
            Assert(report.Outcome == CiAiRepositoryAdmissionOutcome.UpgradeRequired,
                "configured fork identities were not counted independently");
        });
        Check("malformed candidate and configured identities fail closed", () =>
        {
            Assert(Evaluate("https://github.com/Owner/Repo", five, entitlement, now).Outcome ==
                CiAiRepositoryAdmissionOutcome.InvalidRequest, "URL candidate was accepted");
            Assert(Evaluate("Owner/New", new[] { "Owner/../Repo" }, entitlement, now).Outcome ==
                CiAiRepositoryAdmissionOutcome.InvalidRequest, "malformed configured identity was accepted");
        });
        Check("unavailable entitlement refuses new admission but identifies existing repositories", () =>
        {
            var unavailable = new CiAiRepositoryEntitlement("licensee-opaque-1", "Paid", 100,
                now.AddDays(-1), now.AddDays(30), CiAiEntitlementVerificationStatus.Unavailable);
            var report = Evaluate("Owner/New", four, unavailable, now);
            Assert(report.Outcome == CiAiRepositoryAdmissionOutcome.EntitlementUnavailable &&
                report.NoWorkStarted && report.ResultCode == CiAiRepositoryAdmissionEvaluator.EntitlementUnavailableCode,
                "unavailable entitlement did not fail closed");
            Assert(Evaluate("Owner/Repo1", five, unavailable, now).MayStartWork,
                "existing repository was disrupted by entitlement unavailability");
        });
        Check("expired and not-yet-valid entitlements refuse new admission", () =>
        {
            var expired = new CiAiRepositoryEntitlement("licensee-opaque-1", "Paid", 100,
                now.AddDays(-10), now, CiAiEntitlementVerificationStatus.Verified);
            var future = new CiAiRepositoryEntitlement("licensee-opaque-1", "Paid", 100,
                now.AddDays(1), now.AddDays(30), CiAiEntitlementVerificationStatus.Verified);
            Assert(Evaluate("Owner/New", four, expired, now).Outcome == CiAiRepositoryAdmissionOutcome.EntitlementUnavailable,
                "expired entitlement was accepted");
            Assert(Evaluate("Owner/New", four, future, now).Outcome == CiAiRepositoryAdmissionOutcome.EntitlementUnavailable,
                "future entitlement was accepted");
        });
        Check("paid entitlement allows a repository count above the free limit", () =>
        {
            var paid = new CiAiRepositoryEntitlement("licensee-opaque-1", "Growth", 25,
                now.AddDays(-1), now.AddDays(30), CiAiEntitlementVerificationStatus.Verified);
            var ten = Enumerable.Range(1, 10).Select(i => $"Owner/Repo{i}").ToArray();
            Assert(Evaluate("Owner/Repo11", ten, paid, now).MayStartWork,
                "valid paid repository allowance was not honored");
        });
        Check("invalid entitlement configuration is rejected at construction", () =>
        {
            Throws<ArgumentOutOfRangeException>(() => new CiAiRepositoryEntitlement("id", "free", 0,
                now.AddDays(-1), now.AddDays(30), CiAiEntitlementVerificationStatus.Verified));
            Throws<ArgumentException>(() => new CiAiRepositoryEntitlement("id", "free", 5,
                now, now.AddDays(-1), CiAiEntitlementVerificationStatus.Verified));
        });
        Check("upgrade URL must be HTTPS and cannot embed credentials", () =>
        {
            Throws<ArgumentException>(() => new CiAiRepositoryAdmissionRequest("Owner/New", five, "corr",
                now, new Uri("http://example.com/buy")));
            Throws<ArgumentException>(() => new CiAiRepositoryAdmissionRequest("Owner/New", five, "corr",
                now, new Uri("https://user:secret@example.com/buy")));
        });
        return passed;

        void Check(string name, Action action)
        {
            try { action(); passed++; }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"CiAi repository admission contract failed: {name}", exception);
            }
        }
    }

    private static CiAiRepositoryAdmissionReport Evaluate(string candidate, IEnumerable<string> configured,
        CiAiRepositoryEntitlement entitlement, DateTimeOffset now, Uri? upgrade = null) =>
        CiAiRepositoryAdmissionEvaluator.Evaluate(
            new CiAiRepositoryAdmissionRequest(candidate, configured, "correlation-1", now, upgrade), entitlement);

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
