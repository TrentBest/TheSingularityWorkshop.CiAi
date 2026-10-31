using System.Security.Cryptography;
using System.Text;
using TheSingularityWorkshop.CiAi;

internal static class CiWorkflowEvidencePacketV3Tests
{
    private const string EventSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string CheckoutSha = EventSha;
    private const string WorkflowSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public static int Run()
    {
        var provenanceBytes = Encoding.UTF8.GetBytes(CreateProvenance());
        var provenance = CiWorkflowProvenanceRecord.ParseAndValidate(
            provenanceBytes, "TrentBest/CiAi", 42, 2,
            Convert.ToHexString(SHA256.HashData(provenanceBytes)));

        var trusted = CiWorkflowEvidencePacketV3.FromValidatedProvenance(
            CreateObservedRun(), provenance, workflowDefinitionTrusted: true);
        Assert(trusted.ContractVersion == 3 && trusted.Schema == CiWorkflowEvidencePacketV3.CurrentSchema,
            "v3 contract identity was not emitted");
        Assert(trusted.Provenance.WorkflowSha == WorkflowSha &&
            trusted.Provenance.EventSha == EventSha &&
            trusted.Provenance.CheckoutSha == CheckoutSha &&
            trusted.CandidateSha == CheckoutSha && trusted.CheckoutVerified && trusted.IsSuccessful,
            "v3 did not preserve independent workflow, event, and checkout identities");

        var serialized = trusted.ToJson();
        var serializedBytes = Encoding.UTF8.GetBytes(serialized);
        var packetDigest = Convert.ToHexString(SHA256.HashData(serializedBytes));
        var roundTrip = CiWorkflowEvidencePacketV3.FromJson(
            serialized, "TrentBest/CiAi", 42, 2, packetDigest, workflowDefinitionTrusted: true);
        Assert(roundTrip.IsSuccessful && roundTrip.Provenance.WorkflowSha == WorkflowSha &&
            roundTrip.CandidateSha == CheckoutSha,
            "v3 durable round-trip lost validated provenance");

        var (runCollection, jobCollection) = CreateCollections();
        var factoryPacket = CiWorkflowEvidencePacketV3.FromCollections(
            runCollection, jobCollection, 42, 2, DateTimeOffset.Parse("2026-10-31T01:02:00Z"),
            provenance, workflowDefinitionTrusted: true);
        Assert(factoryPacket.IsSuccessful && factoryPacket.Provenance.WorkflowSha == WorkflowSha,
            "v3 collection factory did not bind the exact run attempt to validated provenance");
        var reloadedWithoutTrust = CiWorkflowEvidencePacketV3.FromJson(
            serialized, "TrentBest/CiAi", 42, 2, packetDigest, workflowDefinitionTrusted: false);
        Assert(!reloadedWithoutTrust.IsSuccessful,
            "durable packet restored workflow trust without a host policy decision");
        Throws<InvalidDataException>(() => CiWorkflowEvidencePacketV3.FromJson(
            serialized, "TrentBest/CiAi", 42, 2, new string('0', 64), workflowDefinitionTrusted: true));

        var untrusted = CiWorkflowEvidencePacketV3.FromValidatedProvenance(
            CreateObservedRun(), provenance, workflowDefinitionTrusted: false);
        Assert(!untrusted.IsSuccessful &&
            untrusted.BlockingReasons.Contains("workflow.definition-untrusted"),
            "untrusted workflow definition was accepted as successful");

        Throws<InvalidDataException>(() => CiWorkflowEvidencePacketV3.FromValidatedProvenance(
            CreateObservedRun(runId: 43), provenance, workflowDefinitionTrusted: true));
        Throws<InvalidDataException>(() => CiWorkflowEvidencePacketV3.FromValidatedProvenance(
            CreateObservedRun(workflowPath: ".github/workflows/other.yml"),
            provenance, workflowDefinitionTrusted: true));

        var mismatchedCheckoutBytes = Encoding.UTF8.GetBytes(
            CreateProvenance().Replace(
                "\"checkout\": {\n        \"ref\": \"refs/pull/17/merge\",\n        \"sha\": \"" + CheckoutSha + "\"",
                "\"checkout\": {\n        \"ref\": \"refs/pull/17/merge\",\n        \"sha\": \"cccccccccccccccccccccccccccccccccccccccc\"",
                StringComparison.Ordinal));
        var mismatchedCheckout = CiWorkflowProvenanceRecord.ParseAndValidate(
            mismatchedCheckoutBytes, "TrentBest/CiAi", 42, 2,
            Convert.ToHexString(SHA256.HashData(mismatchedCheckoutBytes)));
        var failClosed = CiWorkflowEvidencePacketV3.FromValidatedProvenance(
            CreateObservedRun(), mismatchedCheckout, workflowDefinitionTrusted: true);
        Assert(!failClosed.CheckoutVerified && !failClosed.IsSuccessful &&
            failClosed.BlockingReasons.Contains("candidate.checkout-unverified"),
            "checkout SHA differing from event SHA was not failed closed");

        Console.WriteLine("PASS: 9 CI workflow evidence packet v3 contract checks.");
        return 9;
    }


    private static (CiWorkflowRunCollection Runs, CiWorkflowJobCollection Jobs) CreateCollections()
    {
        var snapshot = new CiRunSnapshot(
            "TrentBest/CiAi", 42, "CI", EventSha,
            CiRunStatus.Completed, CiRunConclusion.Success,
            new Uri("https://github.com/TrentBest/CiAi/actions/runs/42"),
            DateTimeOffset.Parse("2026-10-31T01:00:00Z"));
        var observation = new CiWorkflowRunObservation(
            snapshot, 2, ".github/workflows/ci.yml", "pull_request",
            Array.Empty<int>()) { WorkflowRef = "refs/heads/development" };
        var runs = new CiWorkflowRunCollection(
            "TrentBest/CiAi", EventSha, new[] { observation }, 1, 1, false);
        var job = new CiWorkflowJobObservation(
            100, 42, 2, "build-and-test", "completed", "success",
            new Uri("https://github.com/TrentBest/CiAi/actions/runs/42/job/100"),
            Array.AsReadOnly(new[]
            {
                new CiWorkflowStepObservation(1, "Build and test", "completed", "success", "2026-10-31T01:00:30Z")
            }));
        var jobs = new CiWorkflowJobCollection("TrentBest/CiAi", 42, 2, new[] { job }, 1, 1, false);
        return (runs, jobs);
    }

    private static CiWorkflowEvidencePacket CreateObservedRun(
        long runId = 42,
        string workflowPath = ".github/workflows/ci.yml")
    {
        var job = new CiWorkflowJobObservation(
            100, runId, 2, "build-and-test", "completed", "success",
            new Uri($"https://github.com/TrentBest/CiAi/actions/runs/{runId}/job/100"),
            Array.AsReadOnly(new[]
            {
                new CiWorkflowStepObservation(1, "Build and test", "completed", "success", "2026-10-31T01:00:00Z")
            }));
        return new CiWorkflowEvidencePacket(
            "TrentBest/CiAi", workflowPath, runId, 2,
            "refs/heads/development", EventSha, "pull_request", "CI",
            CiRunStatus.Completed, CiRunConclusion.Success,
            new Uri($"https://github.com/TrentBest/CiAi/actions/runs/{runId}"),
            DateTimeOffset.Parse("2026-10-31T01:01:00Z"),
            Array.AsReadOnly(new[] { job }), true, true);
    }

    private static string CreateProvenance() => $$"""
    {
      "schema": "{{CiWorkflowProvenanceRecord.CurrentSchema}}",
      "repository": "TrentBest/CiAi",
      "runId": 42,
      "runAttempt": 2,
      "workflow": {
        "ref": "TrentBest/CiAi/.github/workflows/ci.yml@refs/heads/development",
        "sha": "{{WorkflowSha}}"
      },
      "event": {
        "name": "pull_request",
        "ref": "refs/pull/17/merge",
        "sha": "{{EventSha}}",
        "pullRequest": {
          "number": 17,
          "head": { "ref": "refs/heads/feature/fix", "sha": "cccccccccccccccccccccccccccccccccccccccc" },
          "base": { "ref": "refs/heads/development", "sha": "dddddddddddddddddddddddddddddddddddddddd" }
        }
      },
      "checkout": {
        "ref": "refs/pull/17/merge",
        "sha": "{{CheckoutSha}}"
      }
    }
    """;

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
