using System.Security.Cryptography;
using System.Text;
using TheSingularityWorkshop.CiAi;

internal static class CiAiRepositoryCompositionTests
{
    public static int Run()
    {
        var configuration = CiAi
            .WithBadge(Badges.SithPolicy())
            .WithDependency(CiAi.Resolve("TrentBest/TheSingularityWorkshop.FSM_API", "runtime"))
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

        Throws<InvalidOperationException>(() =>
            CiAi.WithBadge(Badges.SithPolicy()).WithBadge(Badges.SithPolicy()));
        Throws<ArgumentException>(() => CiAi.Resolve("not-a-repository"));

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
            profile.RequiredJobKey == "build-and-contracts",
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
        const string eventSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string checkoutSha = "cccccccccccccccccccccccccccccccccccccccc";
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
              "head": { "ref": "feature/integration", "sha": "{{eventSha}}" },
              "base": { "ref": "development", "sha": "{{checkoutSha}}" }
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
            repository, workflowPath, "refs/heads/development", checkoutSha);
        var approvedBytes = Encoding.UTF8.GetBytes("name: Build\non: [push, pull_request]\n");
        var trusted = trustPolicy.Evaluate(provenance, approvedBytes, approvedBytes);
        Assert(trusted.IsTrusted && trusted.IsFor(provenance) &&
            trusted.ReasonCode == "workflow-definition.matches-approved-protected-content",
            "matching protected workflow content was not trusted with exact identity");
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
