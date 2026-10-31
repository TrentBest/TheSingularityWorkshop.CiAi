using System.Net;
using System.Text;
using System.Text.Json;
using TheSingularityWorkshop.CiAi;

internal static class FoundryArtifactReadyNoticeDeliveryCoordinatorTests
{
    public static async Task<int> RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "ciai-foundry-delivery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var outbox = new FileFoundryArtifactReadyNoticeOutbox(Path.Combine(root, "notices.jsonl"));
            var ledger = new FileFoundryArtifactIngressReceiptLedger(Path.Combine(root, "receipts.jsonl"));
            var first = new FoundryArtifactReadyNoticeDelivery(CreateNotice("notice-1", "request-1:operation-1:1"));
            var second = new FoundryArtifactReadyNoticeDelivery(CreateNotice("notice-2", "request-2:operation-2:1"));
            Assert(outbox.Enqueue(first) == FoundryArtifactNoticeEnqueueOutcome.Enqueued &&
                outbox.Enqueue(second) == FoundryArtifactNoticeEnqueueOutcome.Enqueued,
                "test notices were not durably enqueued");

            var sendCount = 0;
            using (var http = new HttpClient(new StubHandler(async (request, cancellationToken) =>
            {
                sendCount++;
                var payload = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var document = JsonDocument.Parse(payload);
                var noticeId = document.RootElement.GetProperty("noticeId").GetString()!;
                var idempotencyKey = document.RootElement.GetProperty("manifest").GetProperty("idempotencyKey").GetString()!;
                return Response(HttpStatusCode.Accepted,
                    Receipt(noticeId, idempotencyKey, "receipt-" + noticeId, "accepted"));
            })))
            {
                var coordinator = new FoundryArtifactReadyNoticeDeliveryCoordinator(
                    outbox, ledger, new FoundryArtifactReadyNoticeClient(http, Endpoint(), new FixtureCredentials()));
                var firstPass = await coordinator.DeliverPendingAsync(maximumDeliveries: 1);
                Assert(firstPass.PendingAtStart == 2 && firstPass.Attempted == 1 &&
                    firstPass.Acknowledged == 1 && firstPass.RemainingPending == 1,
                    "bounded first pass did not persist exactly one accepted notice");
                Assert(firstPass.Attempts[0].ReceiptRecordOutcome == FoundryArtifactIngressReceiptRecordOutcome.Recorded &&
                    ledger.ReadAcknowledgedNoticeIds().Contains(first.NoticeId),
                    "accepted receipt was not persisted against the exact first notice");
                Assert(firstPass.Attempts[0].StatusCode == HttpStatusCode.Accepted &&
                    firstPass.Attempts[0].Receipt?.ReceiptId == "receipt-" + first.NoticeId,
                    "delivery result did not preserve exact HTTP status and receiver receipt");

                var secondPass = await coordinator.DeliverPendingAsync(maximumDeliveries: 10);
                Assert(secondPass.PendingAtStart == 1 && secondPass.Attempted == 1 &&
                    secondPass.Acknowledged == 1 && secondPass.RemainingPending == 0,
                    "second pass did not drain the remaining notice");
                Assert(sendCount == 2, "coordinator did not make exactly one network attempt per pending notice");

                var emptyPass = await coordinator.DeliverPendingAsync();
                Assert(emptyPass.PendingAtStart == 0 && emptyPass.Attempted == 0 &&
                    emptyPass.Acknowledged == 0 && emptyPass.RemainingPending == 0 && sendCount == 2,
                    "acknowledged notices were replayed");
                AssertThrows<ArgumentOutOfRangeException>(() => coordinator.DeliverPendingAsync(0).GetAwaiter().GetResult());
                AssertThrows<ArgumentOutOfRangeException>(() => coordinator.DeliverPendingAsync(101).GetAwaiter().GetResult());
            }

            var mixedRoot = Path.Combine(root, "mixed");
            Directory.CreateDirectory(mixedRoot);
            var mixedOutbox = new FileFoundryArtifactReadyNoticeOutbox(Path.Combine(mixedRoot, "notices.jsonl"));
            var mixedLedger = new FileFoundryArtifactIngressReceiptLedger(Path.Combine(mixedRoot, "receipts.jsonl"));
            mixedOutbox.Enqueue(new FoundryArtifactReadyNoticeDelivery(CreateNotice("invalid-1", "invalid-request-1")));
            mixedOutbox.Enqueue(new FoundryArtifactReadyNoticeDelivery(CreateNotice("valid-2", "valid-request-2")));
            var mixedCount = 0;
            using (var http = new HttpClient(new StubHandler(async (request, cancellationToken) =>
            {
                mixedCount++;
                if (mixedCount == 1)
                    return Response(HttpStatusCode.BadRequest, "{\"code\":\"notice.policy-rejected\"}");
                var payload = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var document = JsonDocument.Parse(payload);
                var noticeId = document.RootElement.GetProperty("noticeId").GetString()!;
                var idempotencyKey = document.RootElement.GetProperty("manifest").GetProperty("idempotencyKey").GetString()!;
                return Response(HttpStatusCode.Accepted,
                    Receipt(noticeId, idempotencyKey, "receipt-" + noticeId, "accepted"));
            })))
            {
                var coordinator = new FoundryArtifactReadyNoticeDeliveryCoordinator(
                    mixedOutbox, mixedLedger, new FoundryArtifactReadyNoticeClient(http, Endpoint(), new FixtureCredentials()));
                var batch = await coordinator.DeliverPendingAsync();
                Assert(batch.Attempted == 2 && batch.Acknowledged == 1 && batch.RemainingPending == 1 &&
                    batch.Attempts[0].Outcome == FoundryArtifactIngressOutcome.InvalidRequest &&
                    batch.Attempts[1].Outcome == FoundryArtifactIngressOutcome.Accepted,
                    "notice-specific rejection incorrectly blocked an independent pending notice");
                Assert(mixedCount == 2 && mixedLedger.ReadAcknowledgedNoticeIds().Contains("valid-2") &&
                    !mixedLedger.ReadAcknowledgedNoticeIds().Contains("invalid-1"),
                    "coordinator did not persist only the independently accepted notice");
            }

            var retryRoot = Path.Combine(root, "retry");
            Directory.CreateDirectory(retryRoot);
            var retryOutbox = new FileFoundryArtifactReadyNoticeOutbox(Path.Combine(retryRoot, "notices.jsonl"));
            var retryLedger = new FileFoundryArtifactIngressReceiptLedger(Path.Combine(retryRoot, "receipts.jsonl"));
            retryOutbox.Enqueue(new FoundryArtifactReadyNoticeDelivery(CreateNotice("retry-1", "retry-request-1")));
            retryOutbox.Enqueue(new FoundryArtifactReadyNoticeDelivery(CreateNotice("retry-2", "retry-request-2")));
            var retryCount = 0;
            using (var http = new HttpClient(new StubHandler((_, _) =>
            {
                retryCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(9)) }
                });
            })))
            {
                var coordinator = new FoundryArtifactReadyNoticeDeliveryCoordinator(
                    retryOutbox, retryLedger, new FoundryArtifactReadyNoticeClient(http, Endpoint(), new FixtureCredentials()));
                var batch = await coordinator.DeliverPendingAsync();
                Assert(batch.Attempted == 1 && batch.Acknowledged == 0 && batch.RemainingPending == 2 &&
                    batch.Attempts[0].Outcome == FoundryArtifactIngressOutcome.RateLimited &&
                    batch.Attempts[0].RetryAfter == TimeSpan.FromSeconds(9),
                    "rate limit did not stop the batch and preserve Retry-After");
                Assert(batch.Attempts[0].StatusCode == HttpStatusCode.TooManyRequests,
                    "rate-limit HTTP status was not preserved in the attempt result");
                Assert(retryCount == 1, "coordinator continued sending after a global back-pressure response");
            }

            var policyRoot = Path.Combine(root, "policy");
            Directory.CreateDirectory(policyRoot);
            var policyOutbox = new FileFoundryArtifactReadyNoticeOutbox(Path.Combine(policyRoot, "notices.jsonl"));
            var policyLedger = new FileFoundryArtifactIngressReceiptLedger(Path.Combine(policyRoot, "receipts.jsonl"));
            policyOutbox.Enqueue(new FoundryArtifactReadyNoticeDelivery(CreateNotice("policy-rejected", "policy-request-1")));
            policyOutbox.Enqueue(new FoundryArtifactReadyNoticeDelivery(CreateNotice("after-policy-rejection", "policy-request-2")));
            var policySendCount = 0;
            using (var http = new HttpClient(new StubHandler(async (request, cancellationToken) =>
            {
                policySendCount++;
                var payload = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var document = JsonDocument.Parse(payload);
                var noticeId = document.RootElement.GetProperty("noticeId").GetString()!;
                var idempotencyKey = document.RootElement.GetProperty("manifest").GetProperty("idempotencyKey").GetString()!;
                return noticeId == "policy-rejected"
                    ? Response(HttpStatusCode.UnprocessableEntity, "{\"code\":\"artifact.policy-rejected\"}")
                    : Response(HttpStatusCode.Accepted,
                        Receipt(noticeId, idempotencyKey, "receipt-" + noticeId, "accepted"));
            })))
            {
                var coordinator = new FoundryArtifactReadyNoticeDeliveryCoordinator(
                    policyOutbox, policyLedger, new FoundryArtifactReadyNoticeClient(http, Endpoint(), new FixtureCredentials()));
                var batch = await coordinator.DeliverPendingAsync();
                Assert(batch.Attempted == 2 && batch.Acknowledged == 1 && batch.RemainingPending == 1,
                    "notice-specific policy rejection incorrectly stopped later independent delivery");
                Assert(batch.Attempts[0].Outcome == FoundryArtifactIngressOutcome.InvalidRequest &&
                    batch.Attempts[1].ReceiptRecordOutcome == FoundryArtifactIngressReceiptRecordOutcome.Recorded,
                    "policy rejection or subsequent accepted receipt was misclassified");
                Assert(policySendCount == 2, "coordinator did not continue after a notice-specific rejection");
            }

            Console.WriteLine("PASS: 19 Foundry notice delivery coordinator checks.");
            return 19;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ArtifactReadyNotice CreateNotice(string noticeId, string idempotencyKey)
    {
        var sha = "abcdef0123456789abcdef0123456789abcdef01";
        var manifest = new ArtifactManifest(
            "request-" + noticeId, "operation-" + noticeId, idempotencyKey,
            "TrentBest/TheSingularityWorkshop.CiAi", "ci.evidence", "2.0.0",
            sha, "artifact-" + noticeId, "v2", sha,
            "SHA-256", new string('a', 64), "application/json", 1024,
            new Uri("https://artifacts.example/evidence/" + noticeId),
            DateTimeOffset.Parse("2026-10-31T00:00:00Z"),
            DateTimeOffset.Parse("2026-10-31T01:00:00Z"),
            "temporary", "foundry-myvr", 42);
        return new ArtifactReadyNotice(1, noticeId, "foundry-myvr", manifest,
            DateTimeOffset.Parse("2026-10-31T00:01:00Z"));
    }

    private static string Receipt(string noticeId, string idempotencyKey, string receiptId, string outcome) =>
        JsonSerializer.Serialize(new
        {
            contract = FoundryArtifactReadyNoticeClient.Contract,
            receiptId,
            noticeId,
            idempotencyKey,
            outcome,
            acceptedAt = "2026-10-31T00:02:00Z"
        });

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Uri Endpoint() =>
        new("https://foundry.example" + FoundryArtifactReadyNoticeClient.EndpointPath);

    private sealed class FixtureCredentials : IFoundryIngressCredentialProvider
    {
        public ValueTask<string> GetBearerTokenAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult("fixture-token");
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
