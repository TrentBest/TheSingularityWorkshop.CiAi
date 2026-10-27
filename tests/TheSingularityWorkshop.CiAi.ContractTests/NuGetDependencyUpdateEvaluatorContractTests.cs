using System.Net;
using System.Text;
using TheSingularityWorkshop.CiAi;

internal static class NuGetDependencyUpdateEvaluatorContractTests
{
    public static async Task<int> RunAsync()
    {
        var passed = 0;
        await CheckAsync("evaluator chooses the highest observed version before applying policy", async () =>
        {
            var catalog = new StubCatalog("Owner.Library", "1.2.4", "1.2.3", "1.2.10", "1.3.0");
            var evaluator = new NuGetDependencyUpdateEvaluator(catalog);
            var result = await evaluator.EvaluateAsync(new DependencyUpdatePolicy(
                "Owner.Library", DependencyUpdateMode.AutomaticWithinPolicy,
                DependencyUpdateAllowedScope.Patch), "1.2.3");
            Assert(result.Candidate.CandidateVersion == "1.3.0",
                "evaluator did not select the highest observed version");
            Assert(result.Decision.Disposition == DependencyUpdateDisposition.Blocked &&
                result.Decision.ReasonCode == "dependency.update-outside-policy",
                "evaluator incorrectly fell back to an older in-policy patch");
            Assert(catalog.LastPackageId == "Owner.Library", "policy package ID was not observed");
        });

        await CheckAsync("evaluator sends an in-scope candidate through policy", async () =>
        {
            var catalog = new StubCatalog("Owner.Library", "1.2.4", "1.2.5", "1.2.3");
            var result = await new NuGetDependencyUpdateEvaluator(catalog).EvaluateAsync(
                new DependencyUpdatePolicy("Owner.Library", DependencyUpdateMode.RequireApproval), "1.2.3");
            Assert(result.Candidate.CandidateVersion == "1.2.5", "highest patch was not selected");
            Assert(result.Decision.Disposition == DependencyUpdateDisposition.AwaitingApproval,
                "approval policy was not applied to feed-derived candidate");
            Assert(result.Observation.Resource.AbsoluteUri.Contains("owner.library/index.json",
                StringComparison.Ordinal), "feed resource provenance was lost");
        });

        await CheckAsync("evaluator blocks when any feed version cannot be classified", async () =>
        {
            var catalog = new StubCatalog("Owner.Library", "1.2.4", "legacy-version");
            var result = await new NuGetDependencyUpdateEvaluator(catalog).EvaluateAsync(
                new DependencyUpdatePolicy("Owner.Library", DependencyUpdateMode.MonitorOnly), "1.2.3");
            Assert(result.Decision.Disposition == DependencyUpdateDisposition.Blocked &&
                result.Decision.ReasonCode == "dependency.catalog-contains-unsupported-version",
                "unsupported version did not fail closed");
        });

        await CheckAsync("evaluator blocks mismatched catalog identity", async () =>
        {
            var catalog = new StubCatalog("Other.Library", "1.2.4");
            var result = await new NuGetDependencyUpdateEvaluator(catalog).EvaluateAsync(
                new DependencyUpdatePolicy("Owner.Library", DependencyUpdateMode.MonitorOnly), "1.2.3");
            Assert(result.Decision.ReasonCode == "dependency.catalog-package-id-mismatch",
                "mismatched feed identity was accepted");
        });

        await CheckAsync("evaluator reports no newer version without authorizing work", async () =>
        {
            var catalog = new StubCatalog("Owner.Library", "1.2.2", "1.2.3");
            var result = await new NuGetDependencyUpdateEvaluator(catalog).EvaluateAsync(
                new DependencyUpdatePolicy("Owner.Library", DependencyUpdateMode.AutomaticWithinPolicy), "1.2.3");
            Assert(result.Decision.Disposition == DependencyUpdateDisposition.Blocked &&
                result.Decision.ReasonCode == "dependency.no-newer-version-observed",
                "no-update observation was not kept blocked");
        });

        return passed;

        async Task CheckAsync(string name, Func<Task> action)
        {
            await action();
            passed++;
            Console.WriteLine($"PASS NuGet dependency update evaluator {passed}: {name}");
        }
    }

    private sealed class StubCatalog(string packageId, params string[] versions) : INuGetVersionCatalog
    {
        public string? LastPackageId { get; private set; }

        public Task<NuGetVersionCatalogObservation> ObserveAsync(
            string requestedPackageId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastPackageId = requestedPackageId;
            return Task.FromResult(new NuGetVersionCatalogObservation(
                packageId,
                new Uri("https://api.nuget.org/v3-flatcontainer/"),
                new Uri("https://api.nuget.org/v3-flatcontainer/owner.library/index.json"),
                DateTimeOffset.Parse("2026-10-27T00:00:00Z"),
                Array.AsReadOnly(versions)));
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
