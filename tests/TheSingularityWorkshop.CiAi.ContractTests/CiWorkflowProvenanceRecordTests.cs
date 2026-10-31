using System.Security.Cryptography;
using System.Text;
using TheSingularityWorkshop.CiAi;

internal static class CiWorkflowProvenanceRecordTests
{
    private const string WorkflowSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string EventSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string PrHeadSha = "cccccccccccccccccccccccccccccccccccccccc";
    private const string PrBaseSha = "dddddddddddddddddddddddddddddddddddddddd";
    private const string CheckoutSha = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";

    public static int Run()
    {
        var bytes = Encoding.UTF8.GetBytes(CreatePullRequestJson());
        var digest = Convert.ToHexString(SHA256.HashData(bytes));
        var record = CiWorkflowProvenanceRecord.ParseAndValidate(
            bytes, "TrentBest/CiAi", 42, 2, digest);

        Assert(record.WorkflowSha == WorkflowSha && record.EventSha == EventSha &&
            record.PullRequest is { HeadSha: var headSha, BaseSha: var baseSha } &&
            headSha == PrHeadSha && baseSha == PrBaseSha && record.CheckoutSha == CheckoutSha,
            "workflow, event, PR source/base, and tested checkout identities were conflated");
        Assert(record.WorkflowPath == ".github/workflows/ci.yml" && record.WorkflowRef ==
            "TrentBest/CiAi/.github/workflows/ci.yml@refs/heads/development",
            "full github.workflow_ref value was not preserved");
        Assert(record.EventRef == "refs/pull/17/merge" &&
            record.CheckoutRef == "refs/pull/17/merge",
            "merge event and actual checkout refs were not independently retained");

        Throws<InvalidDataException>(() => CiWorkflowProvenanceRecord.ParseAndValidate(
            bytes, "TrentBest/CiAi", 43, 2, digest));
        Throws<InvalidDataException>(() => CiWorkflowProvenanceRecord.ParseAndValidate(
            bytes, "TrentBest/CiAi", 42, 1, digest));
        Throws<InvalidDataException>(() => CiWorkflowProvenanceRecord.ParseAndValidate(
            bytes, "TrentBest/Other", 42, 2, digest));
        Throws<InvalidDataException>(() => CiWorkflowProvenanceRecord.ParseAndValidate(
            bytes, "TrentBest/CiAi", 42, 2, new string('0', 64)));

        var foreignWorkflowRepository = Encoding.UTF8.GetBytes(CreatePullRequestJson()
            .Replace("TrentBest/CiAi/.github/workflows/ci.yml", "Other/Repo/.github/workflows/ci.yml", StringComparison.Ordinal));
        Throws<InvalidDataException>(() => CiWorkflowProvenanceRecord.ParseAndValidate(
            foreignWorkflowRepository, "TrentBest/CiAi", 42, 2,
            Convert.ToHexString(SHA256.HashData(foreignWorkflowRepository))));

        var wrongSchema = Encoding.UTF8.GetBytes(CreatePullRequestJson()
            .Replace(CiWorkflowProvenanceRecord.CurrentSchema, "ciai.workflow-provenance.v99", StringComparison.Ordinal));
        Throws<InvalidDataException>(() => CiWorkflowProvenanceRecord.ParseAndValidate(
            wrongSchema, "TrentBest/CiAi", 42, 2,
            Convert.ToHexString(SHA256.HashData(wrongSchema))));

        var inconsistentPullRequest = Encoding.UTF8.GetBytes(CreatePullRequestJson()
            .Replace("\"name\": \"pull_request\"", "\"name\": \"push\"", StringComparison.Ordinal));
        Throws<InvalidDataException>(() => CiWorkflowProvenanceRecord.ParseAndValidate(
            inconsistentPullRequest, "TrentBest/CiAi", 42, 2,
            Convert.ToHexString(SHA256.HashData(inconsistentPullRequest))));

        var missingCheckoutSha = Encoding.UTF8.GetBytes(CreatePullRequestJson()
            .Replace($"\"sha\": \"{CheckoutSha}\"", "\"sha\": \"\"", StringComparison.Ordinal));
        Throws<InvalidDataException>(() => CiWorkflowProvenanceRecord.ParseAndValidate(
            missingCheckoutSha, "TrentBest/CiAi", 42, 2,
            Convert.ToHexString(SHA256.HashData(missingCheckoutSha))));

        var dispatch = Encoding.UTF8.GetBytes(CreateDispatchJson());
        var dispatchRecord = CiWorkflowProvenanceRecord.ParseAndValidate(
            dispatch, "TrentBest/CiAi", 42, 2,
            Convert.ToHexString(SHA256.HashData(dispatch)));
        Assert(dispatchRecord.PullRequest is null && dispatchRecord.EventName == "workflow_dispatch",
            "workflow_dispatch provenance with no PR was not accepted");

        Console.WriteLine("PASS: 12 workflow provenance artifact contract checks.");
        return 12;
    }


    private static string CreateDispatchJson() => $$"""
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
        "ref": "refs/heads/feature/fix",
        "sha": "{{CheckoutSha}}"
      }
    }
    """;

    private static string CreatePullRequestJson() => $$"""
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
          "head": { "ref": "refs/heads/feature/fix", "sha": "{{PrHeadSha}}" },
          "base": { "ref": "refs/heads/development", "sha": "{{PrBaseSha}}" }
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
