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
        Check("manifest v1 preserves its original wire shape", () =>
        {
            const string json = """{"schema":"ciai.repository.v1","repository":"TrentBest/A","observerProfile":null,"badges":[],"dependencies":[],"extensions":[]}""";
            var serialized = CiAiRepositoryManifest.ParseJson(json).ToJson();
            using var document = System.Text.Json.JsonDocument.Parse(serialized);
            Assert(!document.RootElement.TryGetProperty("relationships", out _) &&
                !document.RootElement.TryGetProperty("adp", out _),
                "v1 serialization leaked v2-only fields");
        });
        Check("manifest v2 round-trips the bidirectional raWWar SURPS graph without enabling ADP", () =>
        {
            const string json = """
            {
              "schema": "ciai.repository.v2",
              "repository": "TrentBest/TheSingularityWorkshop.CiAi",
              "observerProfile": "ciai-default",
              "badges": [],
              "dependencies": [],
              "extensions": [],
              "relationships": [
                {
                  "relationshipId": "rawwar-content-contribution",
                  "contractVersion": 1,
                  "kind": "ContentContribution",
                  "sourceRepository": "TrentBest/raWWar",
                  "targetRepository": "TrentBest/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem",
                  "artifactKind": "surps-content-proposal",
                  "authorityRepository": "TrentBest/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem"
                },
                {
                  "relationshipId": "surps-canonical-content-consumption",
                  "contractVersion": 1,
                  "kind": "CanonicalContentConsumption",
                  "sourceRepository": "TrentBest/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem",
                  "targetRepository": "TrentBest/raWWar",
                  "artifactKind": "surps-canonical-content",
                  "authorityRepository": "TrentBest/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem"
                },
                {
                  "relationshipId": "rawwar-experience-validation",
                  "contractVersion": 1,
                  "kind": "ExperienceValidation",
                  "sourceRepository": "TrentBest/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem",
                  "targetRepository": "TrentBest/raWWar",
                  "artifactKind": "rawwar-experience-candidate",
                  "authorityRepository": "TrentBest/raWWar"
                }
              ],
              "adp": null
            }
            """;
            var first = CiAiRepositoryManifest.ParseJson(json);
            Assert(first.Schema == CiAiRepositoryManifest.CurrentSchemaV2 &&
                first.Definition.Relationships.Count == 3 &&
                !first.Definition.AutonomousDevelopmentEnabled,
                "v2 relationship declarations implicitly enabled autonomous execution");
            var serialized = first.ToJson();
            var second = CiAiRepositoryManifest.ParseJson(serialized);
            Assert(serialized == second.ToJson() &&
                second.Definition.Relationships.Select(x => x.RelationshipId).SequenceEqual(new[]
                {
                    "rawwar-content-contribution",
                    "rawwar-experience-validation",
                    "surps-canonical-content-consumption"
                }),
                "v2 graph serialization was not deterministic or lost a relationship");
        });
        Check("manifest v2 requires explicit bounded ADP settings to enable autonomous development", () =>
        {
            const string json = """
            {
              "schema": "ciai.repository.v2",
              "repository": "TrentBest/A",
              "observerProfile": null,
              "badges": [],
              "dependencies": [],
              "extensions": [],
              "relationships": [],
              "adp": {
                "maximumRepairAttempts": 2,
                "requireHumanApprovalForWrites": true
              }
            }
            """;
            var manifest = CiAiRepositoryManifest.ParseJson(json);
            Assert(manifest.Definition.AutonomousDevelopmentEnabled &&
                manifest.Definition.AdpSettings!.MaximumRepairAttempts == 2 &&
                manifest.Definition.AdpSettings.RequireHumanApprovalForWrites,
                "explicit ADP policy was not preserved");
            Assert(CiAiRepositoryManifest.ParseJson(manifest.ToJson()).Definition.AutonomousDevelopmentEnabled,
                "ADP policy did not survive deterministic serialization");
        });
        Check("manifest v2 rejects unknown relationship kinds and duplicate relationship IDs", () =>
        {
            const string prefix = """{"schema":"ciai.repository.v2","repository":"TrentBest/A","badges":[],"dependencies":[],"extensions":[],"relationships":[""";
            const string edge = """{"relationshipId":"edge","contractVersion":1,"kind":"Unknown","sourceRepository":"TrentBest/B","targetRepository":"TrentBest/C","artifactKind":"content","authorityRepository":"TrentBest/C"}""";
            const string suffix = """],"adp":null}""";
            Throws<InvalidDataException>(() => CiAiRepositoryManifest.ParseJson(prefix + edge + suffix));
            const string validEdge = """{"relationshipId":"edge","contractVersion":1,"kind":"BuildDependency","sourceRepository":"TrentBest/B","targetRepository":"TrentBest/C","artifactKind":"build","authorityRepository":"TrentBest/B"}""";
            Throws<InvalidOperationException>(() => CiAiRepositoryManifest.ParseJson(prefix + validEdge + "," + validEdge + suffix));
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
        Check("raWWar observer profile matches its workflow and artifact contract", () =>
        {
            var profile = CiWorkflowObserverProfile.RaWWar;
            Assert(profile.WorkflowPath == ".github/workflows/build.yml", "raWWar workflow path changed");
            Assert(profile.ProvenanceArtifactName(45546590828, 1) == "rawwar-workflow-provenance-45546590828-1",
                "raWWar provenance artifact identity changed");
            Assert(profile.TestArtifactName(45546590828, 1) == "rawwar-contract-results-45546590828-1",
                "raWWar contract artifact identity changed");
            Assert(profile.ProvenanceEntryName == "workflow-provenance.json" &&
                profile.TrxEntryName == "results.trx" &&
                profile.RequiredJobKey == "build-and-contracts" &&
                profile.RequiredJobName == "build-and-contracts",
                "raWWar evidence entry or required-job identity changed");
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
