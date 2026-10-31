using System.Net;
using System.Text;
using System.Text.Json;
using TheSingularityWorkshop.CiAi;

internal static class FoundryArtifactReadyNoticeClientTests
{
    public static async Task<int> RunAsync()
    {
        var notice = CreateNotice();
        HttpRequestMessage? captured = null;
        using (var http = new HttpClient(new StubHandler((request, _) =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent(Receipt(notice, "accepted"), Encoding.UTF8, "application/json")
            });
        })))
        {
            var sender = new FoundryArtifactReadyNoticeClient(http, Endpoint(),
                new FixtureCredentials("host-issued-short-lived-token"));
            var result = await sender.SendAsync(notice);
            Assert(result.Outcome == FoundryArtifactIngressOutcome.Accepted && result.IsAcknowledged,
                "202 durable receipt was not accepted");
            Assert(result.Receipt?.ReceiptId == "receipt-1", "receipt identity was not retained");
            Assert(captured?.Method == HttpMethod.Post && captured.RequestUri == Endpoint(),
                "sender did not POST to the configured Foundry route");
            Assert(captured?.Headers.Authorization?.Scheme == "Bearer" &&
                captured.Headers.Authorization.Parameter == "host-issued-short-lived-token",
                "host-managed bearer token was not sent in the Authorization header");
            var body = await captured!.Content!.ReadAsStringAsync();
            Assert(body.Contains("\"contractVersion\":1", StringComparison.Ordinal) &&
                body.Contains("\"retrievalUri\":", StringComparison.Ordinal),
                "metadata-only notice was not serialized using the v1 camelCase wire shape");
            Assert(!body.Contains("host-issued-short-lived-token", StringComparison.Ordinal),
                "credential leaked into the request body");
        }

        using (var http = ResponseClient(HttpStatusCode.OK, Receipt(notice, "duplicate")))
        {
            var result = await new FoundryArtifactReadyNoticeClient(http, Endpoint(),
                new FixtureCredentials("token")).SendAsync(notice);
            Assert(result.Outcome == FoundryArtifactIngressOutcome.Duplicate && result.IsAcknowledged,
                "200 exact-duplicate receipt was not accepted");
        }

        using (var http = ResponseClient(HttpStatusCode.Accepted,
            Receipt(notice, "accepted", noticeId: "different-notice")))
        {
            var result = await new FoundryArtifactReadyNoticeClient(http, Endpoint(),
                new FixtureCredentials("token")).SendAsync(notice);
            Assert(result.Outcome == FoundryArtifactIngressOutcome.Rejected &&
                result.ErrorCode == "receipt.provenance-mismatch",
                "receipt for a different notice was accepted");
        }

        using (var http = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)))))
        {
            var result = await new FoundryArtifactReadyNoticeClient(http, Endpoint(),
                new FixtureCredentials("token")).SendAsync(notice);
            Assert(result.Outcome == FoundryArtifactIngressOutcome.AuthenticationRequired && !result.IsAcknowledged,
                "401 did not pause delivery for re-authentication");
        }

        using (var http = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7)) }
            }))))
        {
            var result = await new FoundryArtifactReadyNoticeClient(http, Endpoint(),
                new FixtureCredentials("token")).SendAsync(notice);
            Assert(result.Outcome == FoundryArtifactIngressOutcome.RateLimited &&
                result.RetryAfter == TimeSpan.FromSeconds(7),
                "429 Retry-After was not preserved");
        }

        using (var http = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))))
        {
            var result = await new FoundryArtifactReadyNoticeClient(http, Endpoint(),
                new FixtureCredentials("token")).SendAsync(notice);
            Assert(result.Outcome == FoundryArtifactIngressOutcome.RetryableFailure && !result.IsAcknowledged,
                "5xx was not left retryable");
        }

        using (var http = ResponseClient(HttpStatusCode.Accepted, "{malformed"))
        {
            var result = await new FoundryArtifactReadyNoticeClient(http, Endpoint(),
                new FixtureCredentials("token")).SendAsync(notice);
            Assert(result.Outcome == FoundryArtifactIngressOutcome.Rejected &&
                result.ErrorCode == "receipt.malformed",
                "malformed durable receipt was treated as acknowledgement");
        }

        AssertThrows<ArgumentException>(() => new FoundryArtifactReadyNoticeClient(
            new HttpClient(), new Uri("http://foundry.example" + FoundryArtifactReadyNoticeClient.EndpointPath),
            new FixtureCredentials("token")));
        AssertThrows<ArgumentException>(() => new FoundryArtifactReadyNoticeClient(
            new HttpClient(), new Uri("https://foundry.example/api/v1/other"),
            new FixtureCredentials("token")));

        Console.WriteLine("PASS: 8 Foundry artifact-ready ingress client contract checks.");
        return 8;
    }

    private static HttpClient ResponseClient(HttpStatusCode status, string body) =>
        new(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        })));

    private static Uri Endpoint() => new("https://foundry.example" + FoundryArtifactReadyNoticeClient.EndpointPath);

    private static ArtifactReadyNotice CreateNotice()
    {
        var manifest = new ArtifactManifest(
            "request-1", "operation-1", "TrentBest/SimpleDemos:42:1",
            "TrentBest/CiAi", "ci.evidence", "1.0.0",
            "abcdef0123456789abcdef0123456789abcdef01",
            "artifact-42-1", "v1", "abcdef0123456789abcdef0123456789abcdef01",
            "SHA-256", new string('a', 64), "application/json", 1024,
            new Uri("https://artifacts.example/evidence/42/1"),
            DateTimeOffset.Parse("2026-10-31T00:00:00Z"),
            DateTimeOffset.Parse("2026-10-31T01:00:00Z"),
            "temporary", "foundry-myvr", 42);
        return new ArtifactReadyNotice(1, "notice-42-1", "foundry-myvr", manifest,
            DateTimeOffset.Parse("2026-10-31T00:01:00Z"));
    }

    private static string Receipt(ArtifactReadyNotice notice, string outcome, string? noticeId = null) =>
        JsonSerializer.Serialize(new
        {
            contract = FoundryArtifactReadyNoticeClient.Contract,
            receiptId = "receipt-1",
            noticeId = noticeId ?? notice.NoticeId,
            idempotencyKey = notice.Manifest.IdempotencyKey,
            outcome,
            acceptedAt = "2026-10-31T00:02:00Z"
        });

    private sealed class FixtureCredentials(string token) : IFoundryIngressCredentialProvider
    {
        public ValueTask<string> GetBearerTokenAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(token);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException("Expected " + typeof(TException).Name + ".");
    }
}
