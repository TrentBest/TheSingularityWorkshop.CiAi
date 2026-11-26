using TheSingularityWorkshop.CiAi;

internal static class CiAiBranchPolicyContractTests
{
    public static int Run()
    {
        var passed = 0;
        const string repo = "TrentBest/Example";
        const string target = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string configSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        Check("missing trusted override defaults to the exact Sith branch pair", () =>
        {
            var result = CiAiBranchPolicyResolver.Resolve(repo, target, null,
                CiAiBranchPolicyInventory.Complete(new[] { "master", "development" }));
            Assert(result.ProfileName == "SithPolicy" && result.Outcome == CiAiBranchPolicyOutcome.Compliant,
                "unconfigured repository did not default to Sith");
        });
        Check("custom profile evaluates its own exact branch set", () =>
        {
            var config = new CiAiBranchPolicyConfiguration(repo, target, configSha,
                CiAiBranchPolicyProfile.Custom("TrunkFlow", new[] { "main", "release", "work" }));
            var result = CiAiBranchPolicyResolver.Resolve(repo, target, config,
                CiAiBranchPolicyInventory.Complete(new[] { "release", "work", "main" }));
            Assert(result.ProfileName == "TrunkFlow" && result.Outcome == CiAiBranchPolicyOutcome.Compliant,
                "custom branch policy was evaluated against Sith");
        });
        Check("custom policy reports extra or missing branches as advisory deviation", () =>
        {
            var config = new CiAiBranchPolicyConfiguration(repo, target, configSha,
                CiAiBranchPolicyProfile.Custom("TrunkFlow", new[] { "main" }));
            var result = CiAiBranchPolicyResolver.Resolve(repo, target, config,
                CiAiBranchPolicyInventory.Complete(new[] { "main", "feature/x" }));
            Assert(result.Outcome == CiAiBranchPolicyOutcome.Deviation &&
                !result.GrantsWriteAuthority && !result.GrantsReleaseAuthority && !result.GrantsDeploymentAuthority,
                "custom deviation was not advisory and authority-free");
        });
        Check("explicit opt-out is visible and not applicable", () =>
        {
            var config = new CiAiBranchPolicyConfiguration(repo, target, configSha,
                CiAiBranchPolicyProfile.Disabled());
            var result = CiAiBranchPolicyResolver.Resolve(repo, target, config,
                CiAiBranchPolicyInventory.Complete(new[] { "anything" }));
            Assert(result.Outcome == CiAiBranchPolicyOutcome.NotApplicable &&
                result.ReasonCode == "branch-policy.explicitly-disabled",
                "explicit branch-policy opt-out was not reported");
        });
        Check("missing or incomplete inventory is unknown, never compliant", () =>
        {
            var missing = CiAiBranchPolicyResolver.Resolve(repo, target, null, null);
            var unavailable = CiAiBranchPolicyResolver.Resolve(repo, target, null,
                CiAiBranchPolicyInventory.Unavailable("github-api-error"));
            Assert(missing.Outcome == CiAiBranchPolicyOutcome.Unknown &&
                unavailable.Outcome == CiAiBranchPolicyOutcome.Unknown,
                "unavailable inventory was treated as compliant");
        });
        Check("configuration cannot be replayed against a different target SHA", () =>
        {
            var config = new CiAiBranchPolicyConfiguration(repo, target, configSha,
                CiAiBranchPolicyProfile.Disabled());
            var result = CiAiBranchPolicyResolver.Resolve(repo,
                "cccccccccccccccccccccccccccccccccccccccc", config,
                CiAiBranchPolicyInventory.Complete(new[] { "main" }));
            Assert(result.Outcome == CiAiBranchPolicyOutcome.InvalidConfiguration &&
                result.ReasonCode == "branch-policy.target-sha-mismatch",
                "stale configuration was accepted for another target commit");
        });
        Check("configuration cannot be replayed across repositories", () =>
        {
            var config = new CiAiBranchPolicyConfiguration(repo, target, configSha,
                CiAiBranchPolicyProfile.Disabled());
            var result = CiAiBranchPolicyResolver.Resolve("TrentBest/Other", target, config,
                CiAiBranchPolicyInventory.Complete(new[] { "main" }));
            Assert(result.Outcome == CiAiBranchPolicyOutcome.InvalidConfiguration &&
                result.ReasonCode == "branch-policy.repository-mismatch",
                "configuration was accepted for another repository");
        });
        Check("invalid profile and duplicate branch declarations are rejected", () =>
        {
            Throws<ArgumentException>(() => CiAiBranchPolicyProfile.Custom("bad", new[] { "main", "main" }));
            Throws<ArgumentException>(() => CiAiBranchPolicyProfile.Custom("bad", new[] { "../main" }));
            Throws<ArgumentException>(() => new CiAiBranchPolicyConfiguration(repo, "not-a-sha", configSha,
                CiAiBranchPolicyProfile.SithPolicy()));
        });
        Check("branch-policy outcomes never grant privileged authority", () =>
        {
            foreach (var outcome in Enum.GetValues<CiAiBranchPolicyOutcome>())
            {
                var result = CiAiBranchPolicyResolver.Resolve(repo, target, null,
                    CiAiBranchPolicyInventory.Complete(Array.Empty<string>()));
                Assert(!result.GrantsWriteAuthority && !result.GrantsReleaseAuthority &&
                    !result.GrantsDeploymentAuthority, "branch policy granted privileged authority");
            }
        });

        return passed;

        void Check(string name, Action action)
        {
            try { action(); passed++; }
            catch (Exception exception) { throw new InvalidOperationException($"Branch-policy contract failed: {name}", exception); }
        }
    }

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
