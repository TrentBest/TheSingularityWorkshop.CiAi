using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using TheSingularityWorkshop.CiAi;

internal static class CiWorkflowProvenanceArtifactReaderTests
{
    private const string HeadSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string WorkflowSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public static async Task<int> RunAsync()
    {
        var payload = Encoding.UTF8.GetBytes($$"""
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
            "name": "push",
            "ref": "refs/heads/development",
            "sha": "{{HeadSha}}",
            "pullRequest": null
          },
          "checkout": {
            "ref": "refs/heads/development",
            "sha": "{{HeadSha}}"
          }
        }
        """);
        var archiveBytes = CreateArchive(payload);
        var archiveDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(archiveBytes)).ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var artifactName = "ciai-workflow-provenance-2";

        using var client = new HttpClient(new StubHandler(request =>
        {
            var requestPath = request.RequestUri!.AbsolutePath;
            if (requestPath.EndsWith("/actions/runs/42/artifacts", StringComparison.Ordinal))
                return Json("{\"total_count\":1,\"artifacts\":[{\"id\":7,\"name\":\"" + artifactName + "\"}]}");
            if (requestPath.EndsWith("/actions/artifacts/7", StringComparison.Ordinal))
                return Json($$"""
                {
                  "id": 7,
                  "name": "{{artifactName}}",
                  "size_in_bytes": {{archiveBytes.Length}},
                  "digest": "{{archiveDigest}}",
                  "expired": false,
                  "created_at": "{{now.AddMinutes(-1):O}}",
                  "expires_at": "{{now.AddHours(1):O}}",
                  "workflow_run": { "id": 42, "head_sha": "{{HeadSha}}" },
                  "archive_download_url": "https://api.github.com/repos/TrentBest/CiAi/actions/artifacts/7/zip"
                }
                """);
            throw new InvalidOperationException("Unexpected artifact API request: " + request.RequestUri);
        }));

        var providerArtifact = await new GitHubActionsArtifactCollector(client)
            .GetRequiredArtifactAsync("TrentBest/CiAi", 42, HeadSha, artifactName);
        var evidence = CiWorkflowProvenanceArtifactReader.Read(
            archiveBytes, providerArtifact, artifactName, "workflow-provenance.json",
            "TrentBest/CiAi", 42, 2, HeadSha, "refs/heads/development",
            WorkflowSha, "push", "refs/heads/development");
        Assert(evidence.Provenance.WorkflowSha == WorkflowSha &&
            evidence.Provenance.EventSha == HeadSha &&
            evidence.Provenance.CheckoutSha == HeadSha &&
            evidence.PayloadSizeBytes == payload.Length,
            "valid provider-bound workflow provenance was not preserved");

        Throws<InvalidDataException>(() => CiWorkflowProvenanceArtifactReader.Read(
            archiveBytes, providerArtifact, artifactName, "workflow-provenance.json",
            "TrentBest/CiAi", 42, 1, HeadSha, "refs/heads/development",
            WorkflowSha, "push", "refs/heads/development"));
        Throws<InvalidDataException>(() => CiWorkflowProvenanceArtifactReader.Read(
            archiveBytes, providerArtifact, artifactName, "workflow-provenance.json",
            "TrentBest/CiAi", 42, 2, HeadSha, "refs/heads/development",
            new string('c', 40), "push", "refs/heads/development"));
        var tampered = archiveBytes.ToArray();
        tampered[^1] ^= 0x01;
        Throws<InvalidDataException>(() => CiWorkflowProvenanceArtifactReader.Read(
            tampered, providerArtifact, artifactName, "workflow-provenance.json",
            "TrentBest/CiAi", 42, 2, HeadSha, "refs/heads/development",
            WorkflowSha, "push", "refs/heads/development"));

        var raWWarArchive = Convert.FromBase64String(
            "UEsDBBQACAAIAElPdV0AAAAAAAAAAAAAAAAYAAAAd29ya2Zsb3ctcHJvdmVuYW5jZS5qc29urZI9b9swEIZ3/QpCc2zxUxI9Jd26FgUyFvw4WUQkUqFIB0Hg/15IlRID9ZiFw+G9e54j+VEgVM6mh1GVJ1Qap9zxLcSXbghvhymGC3jlDRwvpHxYohGmMLsU4vsS/x3Bpx8wpyqq52cVt0z2P215Qpy3kmJBJW/2+lNKME6pPCGylnZUeUIfBUIroLs3uTqeXeqzrvaOudLZDfb4Pg6PEbq5mvIwVARXI8QzrCLLZv26lsVcGGE0Jh3RVIMSALRuSMsIlkrUHdQaW9BlgdB1FYML+PRl5dUIy6AF8ifCa4Y57YzN+PsktrZl1q8NtYssKnnUEJcbxA97rQdlbzJfUs4nOEeVXPCVyin4MIY8HyxcYAjTCD4dJjfB4Pxue2vMGsytpKLVbSskaxmBzpLOypbRRgoqmVRWaVZunddPIa1muCt0Q74DtNZyg7UwwLjlilBJdFO3lGtaiwYToxkFzupPYLGf/17N9GBeQk7/fafv/SHFtfgLUEsHCImv2YZ3AQAAOAMAAFBLAQItAxQACAAIAElPdV2Jr9mGdwEAADgDAAAYAAAAAAAAAAAAIACkgQAAAAB3b3JrZmxvdy1wcm92ZW5hbmNlLmpzb25QSwUGAAAAAAEAAQBGAAAAvQEAAAAA");
        var raWWarHead = "3704d9258b88593831efd1fd98327952939adab3";
        var raWWarMerge = "d045c5cb01f1b2bea5ee267183109a56fe6b0deb";
        var raWWarBase = "ddd4c0b5ce34d4a1291b76824b265701cb32e436";
        const string raWWarArtifactName = "rawwar-workflow-provenance-44892052947-1";
        using var raWWarClient = new HttpClient(new StubHandler(request =>
        {
            var requestPath = request.RequestUri!.AbsolutePath;
            if (requestPath.EndsWith("/actions/runs/44892052947/artifacts", StringComparison.Ordinal))
                return Json("{\"total_count\":1,\"artifacts\":[{\"id\":14701779950,\"name\":\"rawwar-workflow-provenance-44892052947-1\"}]}");
            if (requestPath.EndsWith("/actions/artifacts/14701779950", StringComparison.Ordinal))
                return Json("""
                {
                  "id": 14701779950,
                  "name": "rawwar-workflow-provenance-44892052947-1",
                  "size_in_bytes": 537,
                  "digest": "sha256:ee855a4bf0bb934d4baee8dd47e3c0419bd065dce6b2e986047e35542f29fe20",
                  "expired": false,
                  "created_at": "2026-11-21T09:58:19Z",
                  "expires_at": "2099-12-31T00:00:00Z",
                  "workflow_run": { "id": 44892052947, "head_sha": "3704d9258b88593831efd1fd98327952939adab3" },
                  "archive_download_url": "https://api.github.com/repos/TrentBest/raWWar/actions/artifacts/14701779950/zip"
                }
                """);
            throw new InvalidOperationException("Unexpected raWWar artifact API request: " + request.RequestUri);
        }));
        var raWWarArtifact = await new GitHubActionsArtifactCollector(raWWarClient)
            .GetRequiredArtifactAsync("TrentBest/raWWar", 44892052947, raWWarHead, raWWarArtifactName);
        var raWWarEvidence = CiWorkflowProvenanceArtifactReader.Read(
            raWWarArchive, raWWarArtifact, CiWorkflowObserverProfile.RaWWar,
            "TrentBest/raWWar", 44892052947, 1,
            raWWarHead, raWWarMerge, raWWarMerge,
            "refs/pull/10/merge",
            raWWarMerge, "pull_request", "refs/pull/10/merge",
            new CiProviderPullRequestObservation(
                10, "integration/autonomous-development-pipeline", raWWarHead,
                "development", raWWarBase));
        Assert(raWWarEvidence.Provenance.WorkflowSha == raWWarMerge &&
            raWWarEvidence.Provenance.EventSha == raWWarMerge &&
            raWWarEvidence.Provenance.CheckoutSha == raWWarMerge &&
            raWWarEvidence.Provenance.PullRequest?.HeadSha == raWWarHead &&
            raWWarEvidence.Provenance.PullRequest?.BaseSha == raWWarBase &&
            raWWarEvidence.PullRequestHeadBaseVerified &&
            raWWarEvidence.ProviderArtifact.ArtifactId == 14701779950,
            "real raWWar PR provenance fixture did not preserve source, merge, and provider head/base identities");

        Console.WriteLine("PASS: 5 provider-bound workflow provenance artifact checks.");
        return 5;
    }

    private static byte[] CreateArchive(byte[] payload)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("workflow-provenance.json", CompressionLevel.Optimal);
            using var output = entry.Open();
            output.Write(payload);
        }
        return stream.ToArray();
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };

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

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}
