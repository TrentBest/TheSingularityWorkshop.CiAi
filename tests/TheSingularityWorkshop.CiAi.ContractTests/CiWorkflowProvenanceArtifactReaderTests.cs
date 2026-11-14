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
                return Json($"""{"total_count":1,"artifacts":[{"id":7,"name":"{{artifactName}}"}]}""");
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

        Console.WriteLine("PASS: 4 provider-bound workflow provenance artifact checks.");
        return 4;
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
