using TheSingularityWorkshop.CiAi;

internal static class CiWorkflowDomainCatalogContractTests
{
    public static int Run()
    {
        var passed = 0;
        Check("domain catalog covers the identified workflow families", () =>
        {
            Assert(CiWorkflowDomainCatalog.All.Count >= 20, "catalog unexpectedly omits major workflow families");
            Assert(CiWorkflowDomainCatalog.All.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() == CiWorkflowDomainCatalog.All.Count,
                "domain IDs must be unique");
        });
        Check("every domain has stable identity and actionable evidence vocabulary", () =>
        {
            foreach (var domain in CiWorkflowDomainCatalog.All)
            {
                Assert(!string.IsNullOrWhiteSpace(domain.Id) && domain.Id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'),
                    $"invalid stable domain ID: {domain.Id}");
                Assert(!string.IsNullOrWhiteSpace(domain.Name) && !string.IsNullOrWhiteSpace(domain.Purpose),
                    $"domain metadata missing: {domain.Id}");
                Assert(domain.EvidenceKinds.Count > 0 && domain.EvidenceKinds.All(e => !string.IsNullOrWhiteSpace(e)),
                    $"domain evidence vocabulary missing: {domain.Id}");
            }
        });
        Check("domain lookup is exact and deterministic", () =>
        {
            Assert(CiWorkflowDomainCatalog.TryGet("performance", out var domain) && domain!.Name == "Performance and benchmarks",
                "known domain could not be resolved");
            Assert(!CiWorkflowDomainCatalog.TryGet("Performance", out _), "lookup should use stable case-sensitive IDs");
            Throws<KeyNotFoundException>(() => CiWorkflowDomainCatalog.GetRequired("not-a-domain"));
        });
        Check("catalog exposes shared controls without duplicating them into each family", () =>
        {
            var controls = CiWorkflowDomainCatalog.CrossCuttingCapabilities;
            Assert(controls.Contains("typed-outcomes") && controls.Contains("evidence-provenance") &&
                controls.Contains("least-privilege") && controls.Contains("candidate-bound-authorization"),
                "core cross-cutting semantics are missing");
            Assert(controls.Distinct(StringComparer.Ordinal).Count() == controls.Count, "cross-cutting IDs must be unique");
        });
        Check("catalog and evidence labels are immutable to consumers", () =>
        {
            Assert(CiWorkflowDomainCatalog.All is not List<CiWorkflowDomain>, "catalog leaks a mutable list");
            Assert(CiWorkflowDomainCatalog.GetRequired("build-test").EvidenceKinds is not string[],
                "domain leaks a mutable evidence array");
        });
        return passed;

        void Check(string name, Action action)
        {
            action();
            Console.WriteLine($"PASS {name}");
            passed++;
        }

        static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException($"Expected {typeof(T).Name}.");
        }
    }
}
