using System.Security.Cryptography;
using System.Text;
using TheSingularityWorkshop.CiAi;

internal static class CiAiRepositoryCompositionTests
{
    public static int Run()
    {
        var configuration = CiAi
            .WithBadge(Badges.SithPolicy())
            .WithDependency(CiAi.Resolve("FSM_API", "runtime"))
            .ExtendWith(Gauntlet.Registrar())
            .Build("TrentBest/TheSingularityWorkshop.CiAi");

        Assert(configuration.Badges.Count == 1 &&
            configuration.Dependencies.Count == 1 &&
            configuration.Extensions.Single().Id == "Gauntlet.Registrar",
            "fluent repository composition did not preserve enabled capabilities");
        Assert(configuration.Badges[0].ImageUrl(configuration.Repository) ==
            "https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/workflows/sith-policy.yml/badge.svg",
            "Sith badge did not use the live workflow SVG");
        Assert(configuration.ToJson() == configuration.ToJson(),
            "repository manifest serialization is not deterministic");
        var roundTrip = CiAiRepositoryConfiguration.ParseJson(configuration.ToJson());
        Assert(roundTrip.Repository == configuration.Repository &&
            roundTrip.Badges.Single().Id == "sith-policy" &&
            roundTrip.Dependencies.Single().Repository == "TrentBest/FSM_API" &&
            roundTrip.Extensions.Single().Id == "Gauntlet.Registrar",
            "repository manifest parser did not round-trip the fluent configuration");

        var shortNameConfiguration = CiAi
            .WithBadge(Badges.SithPolicy())
            .WithDependency(CiAi.Resolve("OtherRepo", "runtime"))
            .Build("TrentBest/ThisRepo");
        Assert(shortNameConfiguration.Dependencies.Single().Repository == "TrentBest/OtherRepo",
            "short dependency name was not resolved against the configured repository owner");
        var externalConfiguration = CiAi
            .WithDependency(CiAi.Resolve("AnotherOwner/ExternalRepo", "contract"))
            .Build("TrentBest/ThisRepo");
        Assert(externalConfiguration.Dependencies.Single().Repository == "AnotherOwner/ExternalRepo",
            "explicit external owner/repository identity was not preserved");

        var temporaryRoot = Path.Combine(Path.GetTempPath(), "ciai-readmes-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temporaryRoot);
            Directory.CreateDirectory(Path.Combine(temporaryRoot, "docs"));
            Directory.CreateDirectory(Path.Combine(temporaryRoot, ".git"));
            Directory.CreateDirectory(Path.Combine(temporaryRoot, "external", "Submodule"));
            File.WriteAllText(Path.Combine(temporaryRoot, "README.md"), "# Root\\n");
            File.WriteAllText(Path.Combine(temporaryRoot, "docs", "readme.MD"), "# Nested\\n");
            File.WriteAllText(Path.Combine(temporaryRoot, ".git", "README.md"), "# Git metadata\\n");
            File.WriteAllText(Path.Combine(temporaryRoot, "external", "Submodule", ".git"), "gitdir: ../.git/modules/Submodule");
            File.WriteAllText(Path.Combine(temporaryRoot, "external", "Submodule", "README.md"), "# Submodule\\n");
            Assert(CiAiReadmeBadgeStamper.FindUnstampedRepositoryReadmes(temporaryRoot, configuration).Count == 2,
                "readme checker did not discover case-insensitive nested README.md files or exclude metadata/submodules");
            Assert(CiAiReadmeBadgeStamper.StampRepositoryReadmes(temporaryRoot, configuration) == 2,
                "repository stamper did not update both owned README.md files");
            Assert(CiAiReadmeBadgeStamper.FindUnstampedRepositoryReadmes(temporaryRoot, configuration).Count == 0,
                "readme checker still reports drift after stamping");
            Assert(File.ReadAllText(Path.Combine(temporaryRoot, ".git", "README.md")) == "# Git metadata\\n" &&
                File.ReadAllText(Path.Combine(temporaryRoot, "external", "Submodule", "README.md")) == "# Submodule\\n",
                "README stamping modified Git metadata or a nested submodule");
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }

        Throws<InvalidOperationException>(() =>
            CiAi.WithBadge(Badges.SithPolicy()).WithBadge(Badges.SithPolicy()));
        Throws<ArgumentException>(() => CiAi.Resolve("bad/repository/identity"));

        const string readme = "# Example\n\nIntroductory text.\n\n## More\n";
        var stamped = CiAiReadmeBadgeStamper.Stamp(readme, configuration);
        Assert(stamped.Contains(CiAiReadmeBadgeStamper.StartMarker, StringComparison.Ordinal) &&
            stamped.Contains(CiAiReadmeBadgeStamper.EndMarker, StringComparison.Ordinal) &&
            stamped.Contains("sith-policy.yml/badge.svg", StringComparison.Ordinal),
            "README did not receive the managed live badge block");
        Assert(CiAiReadmeBadgeStamper.Stamp(stamped, configuration) == stamped,
            "README stamping is not idempotent");
        Assert(stamped.Contains("Introductory text.", StringComparison.Ordinal) &&
            stamped.Contains("## More", StringComparison.Ordinal),
            "README stamping changed unmanaged content");
        Throws<InvalidDataException>(() => CiAiReadmeBadgeStamper.Stamp(
            "# Example\n<!-- CIAI:BADGES:START -->\nmissing end", configuration));

        var profile = CiWorkflowObserverProfile.RaWWar;
        Assert(profile.WorkflowPath == ".github/workflows/build.yml" &&
            profile.ProvenanceArtifactName(44892052947, 1) == "rawwar-workflow-provenance-1" &&
            profile.TestArtifactName(44892052947, 1) == "rawwar-contract-results-44892052947-1" &&
            profile.RequiredJobKey == "build-and-contracts" &&
            profile.RequiredJobName == "build-and-contracts",
            "raWWar observer profile did not resolve the exact provider contract");
        Assert(CiWorkflowObserverProfile.CiAiDefault.ProvenanceArtifactName(42, 3) ==
            "ciai-workflow-provenance-3",
            "CiAi default observer profile changed its existing artifact convention");
        Throws<ArgumentException>(() => new CiWorkflowObserverProfile(
            "bad", "../workflow.yml", "provenance-{attempt}", "tests-{runId}",
            "provenance.json", "results.trx", "provider", "job", "Job"));

        const string repository = "TrentBest/raWWar";
        const string workflowPath = ".github/workflows/build.yml";
        const string workflowSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string providerHeadSha = "dddddddddddddddddddddddddddddddddddddddd";
        const string eventSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string baseSha = "cccccccccccccccccccccccccccccccccccccccc";
        const string checkoutSha = eventSha;
        var provenanceJson = $$"""
        {
          "schema": "{{CiWorkflowProvenanceRecord.CurrentSchema}}",
          "repository": "{{repository}}",
          "runId": 123,
          "runAttempt": 1,
          "workflow": {
            "ref": "{{repository}}/{{workflowPath}}@refs/pull/10/merge",
            "sha": "{{workflowSha}}"
          },
          "event": {
            "name": "pull_request",
            "ref": "refs/pull/10/merge",
            "sha": "{{eventSha}}",
            "pullRequest": {
              "number": 10,
              "head": { "ref": "feature/integration", "sha": "{{providerHeadSha}}" },
              "base": { "ref": "development", "sha": "{{baseSha}}" }
            }
          },
          "checkout": {
            "ref": "refs/pull/10/merge",
            "sha": "{{checkoutSha}}"
          }
        }
        """;
        var bytes = Encoding.UTF8.GetBytes(provenanceJson);
        var provenance = CiWorkflowProvenanceRecord.ParseAndValidate(
            bytes, repository, 123, 1,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        var trustPolicy = new CiWorkflowDefinitionTrustPolicy(
            repository, workflowPath, "refs/heads/development", baseSha);
        var approvedBytes = Encoding.UTF8.GetBytes("name: Build\non: [push, pull_request]\n");
        var trusted = trustPolicy.Evaluate(provenance, approvedBytes, approvedBytes);
        Assert(trusted.IsTrusted && trusted.IsFor(provenance) &&
            trusted.ReasonCode == "workflow-definition.matches-approved-protected-content",
            "matching protected workflow content was not trusted with exact identity");
        var pullRequestIdentity = new CiPullRequestProviderIdentity(
            10, "feature/integration", providerHeadSha,
            "development", baseSha, eventSha, new[] { providerHeadSha, baseSha });
        Assert(pullRequestIdentity.Matches(provenance),
            "provider-observed PR head/base and merge-parent identities did not match provenance");
        Throws<ArgumentException>(() => new CiPullRequestProviderIdentity(
            10, "feature/integration", providerHeadSha,
            "development", checkoutSha, eventSha, new[] { providerHeadSha, new string('e', 40) }));
        var observedRun = new CiWorkflowRunEvidenceObservation(
            repository, workflowPath, 123, 1, "refs/pull/10/merge", providerHeadSha,
            "pull_request", "Build", CiRunStatus.Completed, CiRunConclusion.Success,
            new Uri("https://github.com/TrentBest/raWWar/actions/runs/123"),
            DateTimeOffset.Parse("2026-11-21T12:00:00Z"),
            Array.Empty<CiWorkflowJobObservation>(), true, true)
        {
            ProviderPullRequestObservation = new CiProviderPullRequestObservation(
                10, "feature/integration", providerHeadSha, "development", baseSha),
            ProviderPullRequestIdentity = pullRequestIdentity
        };
        var structuralOnlyRun = observedRun with { ProviderPullRequestIdentity = null };
        var structuralOnlyPacket = CiWorkflowEvidencePacket.FromValidatedProvenance(
            structuralOnlyRun, provenance, workflowDefinitionTrusted: false);
        Assert(!structuralOnlyPacket.IsSuccessful && !structuralOnlyPacket.CheckoutVerified &&
            !structuralOnlyPacket.PullRequestIdentityVerified &&
            structuralOnlyPacket.BlockingReasons.Contains("pull-request.provider-identity-unverified") &&
            structuralOnlyPacket.BlockingReasons.Contains("workflow.definition-untrusted"),
            "PR evidence without verified merge parents was not retained as structural, non-actionable evidence");
        Throws<InvalidDataException>(() => CiWorkflowEvidencePacket.FromValidatedProvenance(
            structuralOnlyRun, provenance, trusted, pullRequestIdentity: null));
        var trustedPacket = CiWorkflowEvidencePacket.FromValidatedProvenance(
            observedRun, provenance, trusted, pullRequestIdentity);
        Assert(trustedPacket.WorkflowDefinitionTrusted && trustedPacket.CheckoutVerified &&
            trustedPacket.CandidateSha == eventSha,
            "trusted PR evidence did not preserve the verified merge checkout as the candidate SHA");
        var untrusted = trustPolicy.Evaluate(provenance, Encoding.UTF8.GetBytes("name: candidate workflow"), approvedBytes);
        Assert(!untrusted.IsTrusted &&
            untrusted.ReasonCode == "workflow-definition.content-mismatch" &&
            untrusted.IsFor(provenance),
            "candidate-controlled workflow content mismatch did not fail closed");

        Console.WriteLine("PASS: repository composition, README stamping, observer profiles, and workflow trust policy.");
        return 12;
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
