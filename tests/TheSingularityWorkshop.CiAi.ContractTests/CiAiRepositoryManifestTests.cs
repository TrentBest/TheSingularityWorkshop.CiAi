using TheSingularityWorkshop.CiAi;

internal static class CiAiRepositoryManifestTests
{
    public static int Run()
    {
        var passed = 0;
        Check("manifest parses repository badges, dependencies, extensions, and observer profile", () =>
        {
            const string json = """
            {
              "schema": "ciai.repository.v1",
              "repository": "TrentBest/TheSingularityWorkshop.CiAi",
              "observerProfile": "ciai-default",
              "badges": [{ "id": "sith-policy", "label": "Sith Policy", "workflowFile": "sith-policy.yml" }],
              "dependencies": [{ "repository": "TrentBest/CommandAi", "capability": "command-vocabulary" }],
              "extensions": [{ "id": "Gauntlet.Registrar", "version": "1" }]
            }
            """;
            var manifest = CiAiRepositoryManifest.ParseJson(json);
            Assert(manifest.Definition.Repository == "TrentBest/TheSingularityWorkshop.CiAi", "repository identity changed");
            Assert(manifest.Definition.Badges.Single().Id == "sith-policy", "badge missing");
            Assert(manifest.Definition.DependencyRepositories.Single() == "TrentBest/CommandAi", "dependency missing");
            Assert(manifest.Definition.Extensions.Single().Id == "Gauntlet.Registrar", "extension missing");
            Assert(manifest.ObserverProfile == "ciai-default", "observer profile missing");
        });
        Check("manifest serialization is deterministic and round-trips", () =>
        {
            const string json = """
            {"schema":"ciai.repository.v1","repository":"TrentBest/A","observerProfile":null,
             "badges":[],"dependencies":[{"repository":"TrentBest/B","capability":"runtime"}],"extensions":[]}
            """;
            var first = CiAiRepositoryManifest.ParseJson(json);
            var serialized = first.ToJson();
            var second = CiAiRepositoryManifest.ParseJson(serialized);
            Assert(serialized == second.ToJson(), "manifest serialization is not deterministic");
            Assert(second.Definition.DependencyRepositories.Single() == "TrentBest/B", "round-trip lost dependency");
        });
        Check("manifest rejects duplicate and unknown properties", () =>
        {
            Throws<InvalidDataException>(() => CiAiRepositoryManifest.ParseJson(
                """{"schema":"ciai.repository.v1","schema":"ciai.repository.v1","repository":"TrentBest/A","badges":[],"dependencies":[],"extensions":[]}"""));
            Throws<InvalidDataException>(() => CiAiRepositoryManifest.ParseJson(
                """{"schema":"ciai.repository.v1","repository":"TrentBest/A","unexpected":"execute","badges":[],"dependencies":[],"extensions":[]}"""));
        });
        Check("manifest rejects unknown observer profiles and self-dependencies", () =>
        {
            Throws<InvalidDataException>(() => CiAiRepositoryManifest.ParseJson(
                """{"schema":"ciai.repository.v1","repository":"TrentBest/A","observerProfile":"unknown","badges":[],"dependencies":[],"extensions":[]}"""));
            Throws<ArgumentException>(() => CiAiRepositoryManifest.ParseJson(
                """{"schema":"ciai.repository.v1","repository":"TrentBest/A","badges":[],"dependencies":[{"repository":"TrentBest/A"}],"extensions":[]}"""));
        });
        Check("observer profiles expand exact run and attempt identity", () =>
        {
            var profile = CiWorkflowObserverProfile.AnyApp;
            Assert(profile.ProvenanceArtifactName(42, 3) == "ciai-workflow-provenance-42-3", "provenance artifact identity changed");
            Assert(profile.TestArtifactName(42, 3) == "ciai-test-results-42-3-build", "test artifact identity changed");
            Throws<ArgumentException>(() => new CiWorkflowObserverProfile("bad", "../workflow.yml",
                "a-{attempt}", "b-{attempt}", "proof.json", "test.trx", "provider", "job", "Job"));
        });
        return passed;

        void Check(string name, Action action)
        {
            try { action(); passed++; }
            catch (Exception exception) { throw new InvalidOperationException($"CiAi repository manifest contract failed: {name}", exception); }
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
