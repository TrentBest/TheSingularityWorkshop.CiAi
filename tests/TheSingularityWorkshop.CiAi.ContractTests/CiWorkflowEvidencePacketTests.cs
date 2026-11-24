using System.Security.Cryptography;
using System.Text;
using TheSingularityWorkshop.CiAi;

internal static class CiWorkflowEvidencePacketTests
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

        var trusted = CiWorkflowEvidencePacket.FromValidatedProvenance(
            CreateObservedRun(), provenance, workflowDefinitionTrusted: true);
        Assert(trusted.ContractVersion == 1 && trusted.Schema == CiWorkflowEvidencePacket.CurrentSchema,
            "v1 contract identity was not emitted");
        Assert(trusted.Provenance.WorkflowSha == WorkflowSha &&
            trusted.Provenance.EventSha == EventSha &&
            trusted.Provenance.CheckoutSha == CheckoutSha &&
            trusted.CandidateSha == CheckoutSha && trusted.CheckoutVerified && trusted.IsSuccessful,
            "v1 did not preserve independent workflow, event, and checkout identities");

        var serialized = trusted.ToJson();
        using (var document = System.Text.Json.JsonDocument.Parse(serialized))
        {
            var observedRun = document.RootElement.GetProperty("observedRun");
            Assert(!observedRun.TryGetProperty("schema", out _) &&
                !observedRun.TryGetProperty("contractVersion", out _),
                "raw run/job observations exposed a second independently versioned contract");
            Assert(observedRun.TryGetProperty("jobs", out _) &&
                observedRun.TryGetProperty("runId", out _),
                "canonical v1 envelope omitted raw run/job observation facts");
        }
        var serializedBytes = Encoding.UTF8.GetBytes(serialized);
        var packetDigest = Convert.ToHexString(SHA256.HashData(serializedBytes));
        var roundTrip = CiWorkflowEvidencePacket.FromJson(
            serialized, "TrentBest/CiAi", 42, 2, packetDigest, workflowDefinitionTrusted: true);
        Assert(roundTrip.IsSuccessful && roundTrip.Provenance.WorkflowSha == WorkflowSha &&
            roundTrip.CandidateSha == CheckoutSha,
            "v1 durable round-trip lost validated provenance");

        var (runCollection, jobCollection) = CreateCollections();
        var factoryPacket = CiWorkflowEvidencePacket.FromCollections(
            runCollection, jobCollection, 42, 2, DateTimeOffset.Parse("2026-10-31T01:02:00Z"),
            provenance, workflowDefinitionTrusted: true);
        Assert(factoryPacket.IsSuccessful && factoryPacket.Provenance.WorkflowSha == WorkflowSha,
            "v1 collection factory did not preserve the independently observed workflow ref");
        var mismatchedWorkflowRefCollection = runCollection with
        {
            Runs = new[]
            {
                runCollection.Runs[0] with { WorkflowRef = "refs/heads/master" }
            }
        };
        Throws<InvalidDataException>(() => CiWorkflowEvidencePacket.FromCollections(
            mismatchedWorkflowRefCollection, jobCollection, 42, 2,
            DateTimeOffset.Parse("2026-10-31T01:02:00Z"),
            provenance, workflowDefinitionTrusted: true));
        var missingWorkflowRefCollection = runCollection with
        {
            Runs = new[]
            {
                runCollection.Runs[0] with { WorkflowRef = null }
            }
        };
        Throws<InvalidDataException>(() => CiWorkflowEvidencePacket.FromCollections(
            missingWorkflowRefCollection, jobCollection, 42, 2,
            DateTimeOffset.Parse("2026-10-31T01:02:00Z"),
            provenance, workflowDefinitionTrusted: true));

        var dispatchBytes = Encoding.UTF8.GetBytes(CreateDispatchProvenance());
        var dispatchProvenance = CiWorkflowProvenanceRecord.ParseAndValidate(
            dispatchBytes, "TrentBest/CiAi", 42, 2,
            Convert.ToHexString(SHA256.HashData(dispatchBytes)));
        var candidateEvidence = new CiWorkflowCandidateCheckoutEvidence(
            "refs/heads/feature/candidate", "ffffffffffffffffffffffffffffffffffffffff",
            "contract", "Resolve optional branch target to an immutable commit",
            "Verify checkout provenance");
        var dispatchPacket = CiWorkflowEvidencePacket.FromValidatedProvenance(
            CreateDispatchObservedRun(candidateEvidence), dispatchProvenance,
            workflowDefinitionTrusted: true, candidateCheckoutEvidence: candidateEvidence);
        Assert(dispatchPacket.IsSuccessful &&
            dispatchPacket.CandidateRef == "refs/heads/feature/candidate" &&
            dispatchPacket.CandidateSha == "ffffffffffffffffffffffffffffffffffffffff",
            "trusted dispatch candidate checkout was not independently retained");

        var pullRequestCandidateSha = "9999999999999999999999999999999999999999";
        var pullRequestCandidateRef = "refs/heads/adp/exact-candidate";
        var pullRequestProvenanceBytes = Encoding.UTF8.GetBytes(CreatePullRequestCandidateProvenance(
            pullRequestCandidateRef, pullRequestCandidateSha));
        var pullRequestProvenance = CiWorkflowProvenanceRecord.ParseAndValidate(
            pullRequestProvenanceBytes, "TrentBest/CiAi", 42, 2,
            Convert.ToHexString(SHA256.HashData(pullRequestProvenanceBytes)));
        var pullRequestCandidateEvidence = new CiWorkflowCandidateCheckoutEvidence(
            pullRequestCandidateRef, pullRequestCandidateSha, "build-and-test",
            "Checkout source candidate", "Verify exact candidate checkout");
        var pullRequestCandidatePacket = CiWorkflowEvidencePacket.FromValidatedProvenance(
            CreatePullRequestCandidateObservedRun(pullRequestCandidateEvidence),
            pullRequestProvenance, workflowDefinitionTrusted: true,
            candidateCheckoutEvidence: pullRequestCandidateEvidence);
        Assert(pullRequestCandidatePacket.IsSuccessful &&
            pullRequestCandidatePacket.CandidateRef == pullRequestCandidateRef &&
            pullRequestCandidatePacket.CandidateSha == pullRequestCandidateSha,
            "exact PR source-head checkout was not accepted with independent job-step proof");
        var mismatchedPullRequestProvenanceBytes = Encoding.UTF8.GetBytes(
            CreatePullRequestCandidateProvenance(
                pullRequestCandidateRef, pullRequestCandidateSha,
                "8888888888888888888888888888888888888888"));
        var mismatchedPullRequestProvenance = CiWorkflowProvenanceRecord.ParseAndValidate(
            mismatchedPullRequestProvenanceBytes, "TrentBest/CiAi", 42, 2,
            Convert.ToHexString(SHA256.HashData(mismatchedPullRequestProvenanceBytes)));
        Throws<InvalidDataException>(() => CiWorkflowEvidencePacket.FromValidatedProvenance(
            CreatePullRequestCandidateObservedRun(pullRequestCandidateEvidence),
            mismatchedPullRequestProvenance, workflowDefinitionTrusted: true,
            candidateCheckoutEvidence: pullRequestCandidateEvidence));
        var adpCandidateEvidence = new CiWorkflowCandidateCheckoutEvidence(
            "refs/heads/adp/worker-candidate", pullRequestCandidateSha, "build-and-test",
            "Checkout source candidate", "Verify exact candidate checkout");
        Assert(adpCandidateEvidence.CandidateRef == "refs/heads/adp/worker-candidate",
            "ADP worker branches must be valid isolated candidate refs");
        Throws<ArgumentException>(() => new CiWorkflowCandidateCheckoutEvidence(
            "refs/heads/development", pullRequestCandidateSha, "build-and-test",
            "Checkout source candidate", "Verify exact candidate checkout"));
        var reloadedWithoutTrust = CiWorkflowEvidencePacket.FromJson(
            serialized, "TrentBest/CiAi", 42, 2, packetDigest, workflowDefinitionTrusted: false);
        Assert(!reloadedWithoutTrust.IsSuccessful,
            "durable packet restored workflow trust without a host policy decision");
        Throws<InvalidDataException>(() => CiWorkflowEvidencePacket.FromJson(
            serialized, "TrentBest/CiAi", 42, 2, new string('0', 64), workflowDefinitionTrusted: true));

        var untrusted = CiWorkflowEvidencePacket.FromValidatedProvenance(
            CreateObservedRun(), provenance, workflowDefinitionTrusted: false);
        Assert(!untrusted.IsSuccessful &&
            untrusted.BlockingReasons.Contains("workflow.definition-untrusted"),
            "untrusted workflow definition was accepted as successful");

        var wrongEventShaJson = CreateProvenance();
        var eventStart = wrongEventShaJson.IndexOf("\"event\": {", StringComparison.Ordinal);
        var eventShaToken = "\"sha\": \"" + EventSha + "\"";
        var eventShaStart = wrongEventShaJson.IndexOf(eventShaToken, eventStart, StringComparison.Ordinal);
        wrongEventShaJson = wrongEventShaJson.Remove(eventShaStart, eventShaToken.Length)
            .Insert(eventShaStart, "\"sha\": \"eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee\"");
        var wrongEventShaBytes = Encoding.UTF8.GetBytes(wrongEventShaJson);
        var wrongEventShaProvenance = CiWorkflowProvenanceRecord.ParseAndValidate(
            wrongEventShaBytes, "TrentBest/CiAi", 42, 2,
            Convert.ToHexString(SHA256.HashData(wrongEventShaBytes)));
        Throws<InvalidDataException>(() => CiWorkflowEvidencePacket.FromValidatedProvenance(
            CreateObservedRun(), wrongEventShaProvenance, workflowDefinitionTrusted: true));

        Throws<InvalidDataException>(() => CiWorkflowEvidencePacket.FromValidatedProvenance(
            CreateObservedRun(runId: 43), provenance, workflowDefinitionTrusted: true));
        Throws<InvalidDataException>(() => CiWorkflowEvidencePacket.FromValidatedProvenance(
            CreateObservedRun(workflowPath: ".github/workflows/other.yml"),
            provenance, workflowDefinitionTrusted: true));

        Throws<InvalidDataException>(() => CiWorkflowEvidencePacket.FromValidatedProvenance(
            CreateObservedRun(workflowRef: "refs/heads/master"),
            provenance, workflowDefinitionTrusted: true));

        var mismatchedCheckoutJson = CreateProvenance();
        var checkoutStart = mismatchedCheckoutJson.IndexOf("\"checkout\": {", StringComparison.Ordinal);
        var checkoutShaToken = "\"sha\": \"" + CheckoutSha + "\"";
        var checkoutShaStart = mismatchedCheckoutJson.IndexOf(checkoutShaToken, checkoutStart, StringComparison.Ordinal);
        mismatchedCheckoutJson = mismatchedCheckoutJson.Remove(checkoutShaStart, checkoutShaToken.Length)
            .Insert(checkoutShaStart, "\"sha\": \"cccccccccccccccccccccccccccccccccccccccc\"");
        var mismatchedCheckoutBytes = Encoding.UTF8.GetBytes(mismatchedCheckoutJson);
        var mismatchedCheckout = CiWorkflowProvenanceRecord.ParseAndValidate(
            mismatchedCheckoutBytes, "TrentBest/CiAi", 42, 2,
            Convert.ToHexString(SHA256.HashData(mismatchedCheckoutBytes)));
        var failClosed = CiWorkflowEvidencePacket.FromValidatedProvenance(
            CreateObservedRun(), mismatchedCheckout, workflowDefinitionTrusted: true);
        Assert(!failClosed.CheckoutVerified && !failClosed.IsSuccessful &&
            failClosed.BlockingReasons.Contains("candidate.checkout-unverified"),
            "checkout SHA differing from event SHA was not failed closed");

        var requiredChecks = new[]
        {
            new CiWorkflowRequiredCheckBinding("build-and-test", "build-and-test")
        };
        var successfulV2 = CiWorkflowEvidencePacketV2.FromPacket(trusted, requiredChecks);
        Assert(successfulV2.Schema == CiWorkflowEvidencePacketV2.CurrentSchema &&
            successfulV2.EvidenceComplete && successfulV2.IsSuccessful &&
            !successfulV2.HasActionableDiagnostics &&
            successfulV2.DiagnosticsFingerprint is null,
            "v2 did not bind the exact successful required check");
        var v2Json = successfulV2.ToJson(indented: false);
        var v2Reloaded = CiWorkflowEvidencePacketV2.FromJson(
            v2Json, "TrentBest/CiAi", 42, 2, successfulV2.PayloadSha256,
            workflowDefinitionTrusted: true);
        Assert(v2Reloaded.IsSuccessful && v2Reloaded.RequiredChecks.Single().JobId == 100,
            "v2 exact-run packet did not round-trip its provider-observed required job");
        var v2Untrusted = CiWorkflowEvidencePacketV2.FromJson(
            v2Json, "TrentBest/CiAi", 42, 2, successfulV2.PayloadSha256,
            workflowDefinitionTrusted: false);
        Assert(v2Untrusted.EvidenceComplete && !v2Untrusted.IsSuccessful,
            "v2 durable reload restored workflow trust without a host policy decision");
        var missingV2 = CiWorkflowEvidencePacketV2.FromPacket(
            trusted, new[] { new CiWorkflowRequiredCheckBinding("missing-check", "not-a-provider-job") });
        Assert(!missingV2.EvidenceComplete && !missingV2.IsSuccessful &&
            missingV2.RequiredChecks.Single().State == "missing",
            "v2 missing required job did not fail closed");

        var failedJob = new CiWorkflowJobObservation(
            101, 42, 2, "build-and-test", "completed", "failure",
            new Uri("https://github.com/TrentBest/CiAi/actions/runs/42/job/101"),
            Array.AsReadOnly(new[]
            {
                new CiWorkflowStepObservation(1, "Build and test", "completed", "failure", "2026-10-31T01:00:30Z")
            }))
        {
            StartedAt = DateTimeOffset.Parse("2026-10-31T00:59:00Z"),
            CompletedAt = DateTimeOffset.Parse("2026-10-31T01:00:30Z")
        };
        var failedRun = new CiWorkflowRunEvidenceObservation(
            "TrentBest/CiAi", ".github/workflows/ci.yml", 42, 2,
            "refs/heads/development", EventSha, "pull_request", "CI",
            CiRunStatus.Completed, CiRunConclusion.Failure,
            new Uri("https://github.com/TrentBest/CiAi/actions/runs/42"),
            DateTimeOffset.Parse("2026-10-31T01:01:00Z"),
            Array.AsReadOnly(new[] { failedJob }), true, true);
        var failedV1 = CiWorkflowEvidencePacket.FromValidatedProvenance(
            failedRun, provenance, workflowDefinitionTrusted: true);
        var failedV2 = CiWorkflowEvidencePacketV2.FromPacket(failedV1, requiredChecks);
        Assert(failedV2.EvidenceComplete && !failedV2.IsSuccessful &&
            failedV2.HasActionableDiagnostics &&
            failedV2.DiagnosticsFingerprint is { Length: 64 },
            "v2 did not classify a complete exact required-check failure as actionable");
        var failedV2Untrusted = CiWorkflowEvidencePacketV2.FromJson(
            failedV2.ToJson(indented: false), "TrentBest/CiAi", 42, 2,
            failedV2.PayloadSha256, workflowDefinitionTrusted: false);
        Assert(!failedV2Untrusted.HasActionableDiagnostics,
            "untrusted workflow definition was allowed to authorize actionable repair evidence");
        Throws<InvalidDataException>(() => CiWorkflowEvidencePacketV2.FromJson(
            v2Json, "TrentBest/CiAi", 42, 2, new string('0', 64),
            workflowDefinitionTrusted: true));

        Console.WriteLine("PASS: 24 CI workflow evidence packet v1/v2 contract checks.");
        return 24;
    }



    private static CiWorkflowRunEvidenceObservation CreatePullRequestCandidateObservedRun(
        CiWorkflowCandidateCheckoutEvidence candidateEvidence)
    {
        var steps = new[]
        {
            new CiWorkflowStepObservation(1, "Checkout source candidate",
                "completed", "success", "2026-10-31T01:00:10Z"),
            new CiWorkflowStepObservation(2, "Verify exact candidate checkout",
                "completed", "success", "2026-10-31T01:00:20Z"),
            new CiWorkflowStepObservation(3, "Build and test",
                "completed", "success", "2026-10-31T01:00:30Z")
        };
        var job = new CiWorkflowJobObservation(
            100, 42, 2, "build-and-test", "completed", "success",
            new Uri("https://github.com/TrentBest/CiAi/actions/runs/42/job/100"),
            Array.AsReadOnly(steps));
        return new CiWorkflowRunEvidenceObservation(
            "TrentBest/CiAi", ".github/workflows/ci.yml", 42, 2,
            "refs/pull/17/merge", EventSha, "pull_request", "CI",
            CiRunStatus.Completed, CiRunConclusion.Success,
            new Uri("https://github.com/TrentBest/CiAi/actions/runs/42"),
            DateTimeOffset.Parse("2026-10-31T01:01:00Z"),
            Array.AsReadOnly(new[] { job }), true, true,
            candidateCheckoutEvidence: candidateEvidence);
    }

    private static string CreatePullRequestCandidateProvenance(
        string candidateRef, string candidateSha, string? pullRequestHeadSha = null) => $$"""
    {
      "schema": "{{CiWorkflowProvenanceRecord.CurrentSchema}}",
      "repository": "TrentBest/CiAi",
      "runId": 42,
      "runAttempt": 2,
      "workflow": {
        "ref": "TrentBest/CiAi/.github/workflows/ci.yml@refs/pull/17/merge",
        "sha": "{{WorkflowSha}}"
      },
      "event": {
        "name": "pull_request",
        "ref": "refs/pull/17/merge",
        "sha": "{{EventSha}}",
        "pullRequest": {
          "number": 17,
          "head": { "ref": "adp/exact-candidate", "sha": "{{(pullRequestHeadSha ?? candidateSha)}}" },
          "base": { "ref": "development", "sha": "dddddddddddddddddddddddddddddddddddddddd" }
        }
      },
      "checkout": {
        "ref": "{{candidateRef}}",
        "sha": "{{candidateSha}}"
      }
    }
    """;

    private static CiWorkflowRunEvidenceObservation CreateDispatchObservedRun(
        CiWorkflowCandidateCheckoutEvidence candidateEvidence)
    {
        var steps = new[]
        {
            new CiWorkflowStepObservation(1, "Resolve optional branch target to an immutable commit",
                "completed", "success", "2026-10-31T01:00:10Z"),
            new CiWorkflowStepObservation(2, "Verify checkout provenance",
                "completed", "success", "2026-10-31T01:00:20Z")
        };
        var job = new CiWorkflowJobObservation(
            100, 42, 2, "contract", "completed", "success",
            new Uri("https://github.com/TrentBest/CiAi/actions/runs/42/job/100"),
            Array.AsReadOnly(steps));
        return new CiWorkflowRunEvidenceObservation(
            "TrentBest/CiAi", ".github/workflows/ci.yml", 42, 2,
            "refs/heads/development", EventSha, "workflow_dispatch", "CI",
            CiRunStatus.Completed, CiRunConclusion.Success,
            new Uri("https://github.com/TrentBest/CiAi/actions/runs/42"),
            DateTimeOffset.Parse("2026-10-31T01:01:00Z"),
            Array.AsReadOnly(new[] { job }), true, true,
            candidateCheckoutEvidence: candidateEvidence);
    }

    private static string CreateDispatchProvenance() => $$"""
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
        "name": "workflow_dispatch",
        "ref": "refs/heads/development",
        "sha": "{{EventSha}}",
        "pullRequest": null
      },
      "checkout": {
        "ref": "refs/heads/feature/candidate",
        "sha": "ffffffffffffffffffffffffffffffffffffffff"
      }
    }
    """;

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

    private static CiWorkflowRunEvidenceObservation CreateObservedRun(
        long runId = 42,
        string workflowPath = ".github/workflows/ci.yml",
        string workflowRef = "refs/heads/development")
    {
        var job = new CiWorkflowJobObservation(
            100, runId, 2, "build-and-test", "completed", "success",
            new Uri($"https://github.com/TrentBest/CiAi/actions/runs/{runId}/job/100"),
            Array.AsReadOnly(new[]
            {
                new CiWorkflowStepObservation(1, "Build and test", "completed", "success", "2026-10-31T01:00:00Z")
            }));
        return new CiWorkflowRunEvidenceObservation(
            "TrentBest/CiAi", workflowPath, runId, 2,
            workflowRef, EventSha, "pull_request", "CI",
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
          "head": { "ref": "feature/fix", "sha": "cccccccccccccccccccccccccccccccccccccccc" },
          "base": { "ref": "development", "sha": "dddddddddddddddddddddddddddddddddddddddd" }
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
