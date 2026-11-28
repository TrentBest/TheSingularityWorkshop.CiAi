using System.Security.Cryptography;
using System.Text;
using TheSingularityWorkshop.CiAi;

internal static class FileCiAiDependencyRegistrationLedgerStoreTests
{
    private const string WorkflowSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ciai-dependency-ledger-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileCiAiDependencyRegistrationLedgerStore(root);
            var first = CreateSnapshot("0123456789abcdef0123456789abcdef01234567", "TrentBest/Provider");
            var verification = CreateVerification(first, 42);
            var applied = store.ApplyVerifiedSnapshot(verification);
            Assert(verification.SourceRevision == 42 && verification.SourceCommitSha == first.SourceCommitSha &&
                verification.EvidencePayloadSha256.Length == 64,
                "verified source evidence did not retain exact run, SHA, and packet digest");
            AssertThrows<InvalidDataException>(() => CreateVerification(first, 42,
                allowedSourceRefs: new[] { "refs/heads/master" }));
            AssertThrows<InvalidDataException>(() => CreateVerification(first, 42, trustedWorkflow: false));
            AssertThrows<InvalidDataException>(() => CreateVerification(first, 42,
                conclusion: CiRunConclusion.Failure));

            var delivery = CiAiDependencyRegistrationDelivery.FromVerification(verification,
                new[] { "TrentBest/Provider", "TrentBest/PriorDependent" });
            var revisionPath = Path.Combine(root, "inventory", "revision.txt");
            var revisionProvider = new FileCiAiRepositoryInventoryRevisionProvider(revisionPath);
            Assert(revisionProvider.GetNextRevisionAsync().GetAwaiter().GetResult() == 1 &&
                new FileCiAiRepositoryInventoryRevisionProvider(revisionPath)
                    .GetNextRevisionAsync().GetAwaiter().GetResult() == 2,
                "inventory revision counter did not persist monotonic increments across instances");
            File.WriteAllText(revisionPath, "not-a-revision", Encoding.ASCII);
            AssertThrows<InvalidDataException>(() =>
                new FileCiAiRepositoryInventoryRevisionProvider(revisionPath)
                    .GetNextRevisionAsync().GetAwaiter().GetResult());
            var inventory = CiAiRepositoryInventorySnapshot.CreateFromTrustedProvider(
                "github-app-installation:trusted-org-inventory", 9,
                new[] { "TrentBest/Consumer", "TrentBest/Provider", "TrentBest/NewRepository" },
                isComplete: true, new string('b', 64));
            var fanOutRecipients = CiAiDependencyRegistrationFanOutPlanner.CreateRecipients(
                "TrentBest/Consumer", inventory, new[] { delivery });
            Assert(fanOutRecipients.SequenceEqual(new[]
                { "TrentBest/NewRepository", "TrentBest/PriorDependent", "TrentBest/Provider" }),
                "fan-out plan did not combine complete current inventory with former recipients");
            var incompleteInventory = CiAiRepositoryInventorySnapshot.CreateFromTrustedProvider(
                "github-app-installation:trusted-org-inventory", 10,
                new[] { "TrentBest/Provider" }, isComplete: false, new string('c', 64));
            AssertThrows<InvalidDataException>(() =>
                CiAiDependencyRegistrationFanOutPlanner.CreateRecipients(
                    "TrentBest/Consumer", incompleteInventory, new[] { delivery }));
            AssertThrows<ArgumentException>(() =>
                CiAiRepositoryInventorySnapshot.CreateFromTrustedProvider(
                    "trusted", 11, new[] { "TrentBest/Provider", "trentbest/provider" },
                    isComplete: true, new string('d', 64)));
            using var inventoryHttp = new HttpClient(new StubHttpMessageHandler(request =>
            {
                Assert(request.RequestUri!.Host == "api.github.com" &&
                    request.Headers.Authorization?.Parameter == "inventory-token",
                    "inventory provider did not use the authenticated GitHub installation endpoint");
                var firstPage = request.RequestUri.Query.Contains("page=1", StringComparison.Ordinal);
                var pageRepositories = firstPage
                    ? Enumerable.Range(0, 100).Select(index => new { full_name = "TrentBest/Repo" + index.ToString("D3") }).ToArray()
                    : new[] { new { full_name = "TrentBest/Repo100" } };
                var body = System.Text.Json.JsonSerializer.Serialize(new
                {
                    total_count = 101,
                    repositories = pageRepositories
                });
                return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
            }));
            var inventoryProvider = new GitHubInstallationRepositoryInventoryProvider(
                inventoryHttp, new FixedInstallationInventoryTokenProvider(),
                new FixedInventoryRevisionProvider(77), installationId: 1234);
            var githubInventory = inventoryProvider.GetCompleteInventoryAsync().AsTask().GetAwaiter().GetResult();
            Assert(githubInventory.IsComplete && githubInventory.Revision == 77 &&
                githubInventory.Repositories.Count == 101 && githubInventory.EvidencePayloadSha256.Length == 64,
                "GitHub inventory provider did not reconcile all pages and retain evidence identity");
            using var incompleteInventoryHttp = new HttpClient(new StubHttpMessageHandler(_ =>
                new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        total_count = 101,
                        repositories = Enumerable.Range(0, 50)
                            .Select(index => new { full_name = "TrentBest/Partial" + index.ToString("D3") })
                    }), Encoding.UTF8, "application/json")
                }));
            var incompleteInventoryProvider = new GitHubInstallationRepositoryInventoryProvider(
                incompleteInventoryHttp, new FixedInstallationInventoryTokenProvider(),
                new FixedInventoryRevisionProvider(78), installationId: 1234);
            AssertThrows<InvalidDataException>(() =>
                incompleteInventoryProvider.GetCompleteInventoryAsync().AsTask().GetAwaiter().GetResult());
            using var duplicateInventoryHttp = new HttpClient(new StubHttpMessageHandler(_ =>
                new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        total_count = 2,
                        repositories = new[] {
                            new { full_name = "TrentBest/Duplicate" },
                            new { full_name = "trentbest/duplicate" }
                        }
                    }), Encoding.UTF8, "application/json")
                }));
            var duplicateInventoryProvider = new GitHubInstallationRepositoryInventoryProvider(
                duplicateInventoryHttp, new FixedInstallationInventoryTokenProvider(),
                new FixedInventoryRevisionProvider(79), installationId: 1234);
            AssertThrows<InvalidDataException>(() =>
                duplicateInventoryProvider.GetCompleteInventoryAsync().AsTask().GetAwaiter().GetResult());
            var outbox = new FileCiAiDependencyRegistrationOutbox(Path.Combine(root, "outbox", "deliveries.jsonl"));
            Assert(outbox.Enqueue(delivery) == CiAiDependencyRegistrationOutboxEnqueueOutcome.Enqueued,
                "first federated delivery was not enqueued");
            Assert(outbox.Enqueue(delivery) == CiAiDependencyRegistrationOutboxEnqueueOutcome.Duplicate,
                "identical federated delivery replay was not deduplicated");
            var reopenedOutbox = new FileCiAiDependencyRegistrationOutbox(Path.Combine(root, "outbox", "deliveries.jsonl"));
            var reloadedDelivery = reopenedOutbox.ReadAll().Single();
            Assert(reloadedDelivery.Payload == delivery.Payload &&
                reloadedDelivery.PayloadSha256 == delivery.PayloadSha256 &&
                reloadedDelivery.RecipientRepositories.Contains("TrentBest/PriorDependent"),
                "delivery restart replay lost payload integrity or a withdrawal recipient");
            var changedRecipients = CiAiDependencyRegistrationDelivery.FromVerification(verification,
                new[] { "TrentBest/Provider" });
            Assert(reopenedOutbox.Enqueue(changedRecipients) == CiAiDependencyRegistrationOutboxEnqueueOutcome.Conflict,
                "delivery identity was reused with a changed recipient set");
            AssertThrows<System.Text.Json.JsonException>(() =>
                CiAiDependencyRegistrationDelivery.FromJson(delivery.Payload.Replace(
                    "\"schema\":", "\"unexpected\": 1, \"schema\":", StringComparison.Ordinal)));
            var acceptedReceipt = CiAiDependencyRegistrationReceipt.FromApplyResult(delivery,
                "TrentBest/Provider", applied);
            Assert(acceptedReceipt.Status == CiAiDependencyRegistrationReceiptStatus.Accepted &&
                acceptedReceipt.SourceRevision == 42 && acceptedReceipt.SourceCommitSha == first.SourceCommitSha &&
                acceptedReceipt.DeliveryPayloadSha256 == delivery.PayloadSha256,
                "accepted receipt did not bind exact delivery identity and digest");
            Assert(CiAiDependencyRegistrationReceipt.FromJson(acceptedReceipt.Payload).Payload == acceptedReceipt.Payload,
                "receipt JSON round-trip changed its canonical payload");
            var receiptStorePath = Path.Combine(root, "receipts", "receipts.jsonl");
            var receiptStore = new FileCiAiDependencyRegistrationReceiptStore(receiptStorePath);
            Assert(receiptStore.Record(acceptedReceipt) == CiAiDependencyRegistrationReceiptStoreOutcome.Recorded,
                "first receiver receipt was not durably recorded");
            Assert(new FileCiAiDependencyRegistrationReceiptStore(receiptStorePath).Record(acceptedReceipt) ==
                CiAiDependencyRegistrationReceiptStoreOutcome.Duplicate,
                "identical receiver receipt retry was not deduplicated after reopening");
            var contradictoryReceipt = CiAiDependencyRegistrationReceipt.FromApplyResult(delivery,
                "TrentBest/Provider", new CiAiDependencyRegistrationApplyResult(
                    CiAiDependencyRegistrationApplyStatus.Stale, applied.Ledger));
            Assert(receiptStore.Record(contradictoryReceipt) == CiAiDependencyRegistrationReceiptStoreOutcome.Conflict,
                "contradictory receipt reused the same delivery/recipient identity");
            Assert(receiptStore.ReadAll().Single().Payload == acceptedReceipt.Payload,
                "receipt journal did not preserve the accepted receipt");
            AssertThrows<ArgumentException>(() => CiAiDependencyRegistrationReceipt.FromApplyResult(delivery,
                "TrentBest/NotARecipient", applied));
            AssertThrows<System.Text.Json.JsonException>(() =>
                CiAiDependencyRegistrationReceipt.FromJson(acceptedReceipt.Payload.Replace(
                    "\\"schema\\":", "\\"unexpected\\": 1, \\"schema\\":", StringComparison.Ordinal)));
            var receiverLedger = new FileCiAiDependencyRegistrationLedgerStore(Path.Combine(root, "receiver-ledger"));
            var receiverReceipts = new FileCiAiDependencyRegistrationReceiptStore(
                Path.Combine(root, "receiver-receipts", "receipts.jsonl"));
            var receiver = new CiAiDependencyRegistrationReceiver("TrentBest/Provider", receiverLedger,
                receiverReceipts, new FixedDeliveryVerifier(verification));
            var received = receiver.ReceiveAsync(delivery.Payload).AsTask().GetAwaiter().GetResult();
            Assert(received.ApplyStatus == CiAiDependencyRegistrationApplyStatus.Applied &&
                received.Receipt.Status == CiAiDependencyRegistrationReceiptStatus.Accepted,
                "receiver did not independently verify, apply, and acknowledge the delivery");
            var replayed = receiver.ReceiveAsync(delivery.Payload).AsTask().GetAwaiter().GetResult();
            Assert(replayed.ApplyStatus == CiAiDependencyRegistrationApplyStatus.Duplicate &&
                replayed.ReceiptStoreOutcome == CiAiDependencyRegistrationReceiptStoreOutcome.Duplicate,
                "receiver replay did not reconcile idempotently before acknowledging");
            var mismatchedReceiver = new CiAiDependencyRegistrationReceiver("TrentBest/NotARecipient",
                receiverLedger, receiverReceipts, new FixedDeliveryVerifier(verification));
            AssertThrows<InvalidDataException>(() =>
                mismatchedReceiver.ReceiveAsync(delivery.Payload).AsTask().GetAwaiter().GetResult());
            var mismatchedEvidence = new CiAiDependencyRegistrationReceiver("TrentBest/Provider",
                receiverLedger, receiverReceipts, new FixedDeliveryVerifier(CreateVerification(
                    CreateSnapshot("5123456789abcdef0123456789abcdef01234567", "TrentBest/Provider"), 44)));
            AssertThrows<InvalidDataException>(() =>
                mismatchedEvidence.ReceiveAsync(delivery.Payload).AsTask().GetAwaiter().GetResult());
            var retryReceiptStore = new FileCiAiDependencyRegistrationReceiptStore(
                Path.Combine(root, "retry-receipts", "receipts.jsonl"));
            var retryStateStore = new FileCiAiDependencyRegistrationDispatchStateStore(
                Path.Combine(root, "retry-state"));
            var fakeTransport = new FixedDependencyTransport();
            var coordinator = new CiAiDependencyRegistrationDispatchCoordinator(outbox,
                retryReceiptStore, retryStateStore, fakeTransport);
            var dispatchTime = DateTimeOffset.Parse("2026-11-28T00:10:00Z");
            var firstDispatch = coordinator.DispatchDueAsync(dispatchTime).AsTask().GetAwaiter().GetResult();
            Assert(firstDispatch.Count == 2 && firstDispatch.All(item => item.AttemptCount == 1),
                "coordinator did not dispatch each unacknowledged recipient");
            Assert(coordinator.DispatchDueAsync(dispatchTime.AddSeconds(29)).AsTask().GetAwaiter().GetResult().Count == 0,
                "coordinator ignored the initial retry backoff");
            var secondDispatch = coordinator.DispatchDueAsync(dispatchTime.AddSeconds(30)).AsTask().GetAwaiter().GetResult();
            Assert(secondDispatch.Count == 2 && secondDispatch.All(item => item.AttemptCount == 2),
                "coordinator did not retry queued events after backoff");
            retryReceiptStore.Record(acceptedReceipt);
            var afterReceipt = coordinator.DispatchDueAsync(dispatchTime.AddMinutes(2)).AsTask().GetAwaiter().GetResult();
            Assert(afterReceipt.Count == 1 && afterReceipt[0].TargetRepository == "TrentBest/PriorDependent",
                "coordinator did not stop dispatching after a receiver receipt arrived");
            var terminalStore = new FileCiAiDependencyRegistrationDispatchStateStore(
                Path.Combine(root, "terminal-state"));
            var permanent = terminalStore.RecordAttempt(delivery.DeliveryId, "TrentBest/Provider",
                new CiAiDependencyRegistrationTransportResult(
                    CiAiDependencyRegistrationTransportDisposition.PermanentFailure, 403, "github.dispatch-rejected"),
                dispatchTime);
            Assert(permanent.Terminal && permanent.NextAttemptAtUtc is null,
                "permanent dispatch rejection was not made terminal");
            AssertThrows<InvalidOperationException>(() => terminalStore.RecordAttempt(delivery.DeliveryId,
                "TrentBest/Provider", new CiAiDependencyRegistrationTransportResult(
                    CiAiDependencyRegistrationTransportDisposition.Queued, 204, "github.event-queued"),
                dispatchTime.AddMinutes(1)));
            using var queuedHttp = new HttpClient(new StubHttpMessageHandler(request =>
            {
                Assert(request.Method == HttpMethod.Post &&
                    request.RequestUri!.AbsoluteUri == "https://api.github.com/repos/TrentBest/Provider/dispatches",
                    "GitHub adapter did not target the recipient repository dispatch endpoint");
                Assert(request.Headers.Authorization?.Scheme == "Bearer" &&
                    request.Headers.Authorization.Parameter == "installation-token",
                    "GitHub adapter did not use the injected installation token");
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                Assert(body.Contains(delivery.DeliveryId, StringComparison.Ordinal) &&
                    body.Contains(delivery.PayloadSha256, StringComparison.Ordinal) &&
                    body.Contains(GitHubDependencyRegistrationDispatchTransport.EventType, StringComparison.Ordinal),
                    "GitHub dispatch payload omitted delivery identity or digest");
                return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
            }));
            var githubTransport = new GitHubDependencyRegistrationDispatchTransport(
                queuedHttp, new FixedInstallationTokenProvider());
            Assert(githubTransport.DispatchAsync(delivery, "TrentBest/Provider").AsTask().GetAwaiter().GetResult()
                .Disposition == CiAiDependencyRegistrationTransportDisposition.Queued,
                "GitHub HTTP 204 was not classified as queued");
            using var throttledHttp = new HttpClient(new StubHttpMessageHandler(_ =>
                new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests)));
            Assert(new GitHubDependencyRegistrationDispatchTransport(throttledHttp,
                new FixedInstallationTokenProvider()).DispatchAsync(delivery, "TrentBest/Provider")
                .AsTask().GetAwaiter().GetResult().Disposition ==
                CiAiDependencyRegistrationTransportDisposition.RetryableFailure,
                "GitHub rate limiting was not classified as retryable");
            using var forbiddenHttp = new HttpClient(new StubHttpMessageHandler(_ =>
                new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Forbidden)));
            Assert(new GitHubDependencyRegistrationDispatchTransport(forbiddenHttp,
                new FixedInstallationTokenProvider()).DispatchAsync(delivery, "TrentBest/Provider")
                .AsTask().GetAwaiter().GetResult().Disposition ==
                CiAiDependencyRegistrationTransportDisposition.PermanentFailure,
                "GitHub permission rejection was not classified as permanent");
            AssertThrows<ArgumentException>(() => githubTransport.DispatchAsync(delivery,
                "TrentBest/NotARecipient").AsTask().GetAwaiter().GetResult());
            var remoteReceiptStore = new FileCiAiDependencyRegistrationReceiptStore(
                Path.Combine(root, "remote-receipts", "receipts.jsonl"));
            var receiptIngestor = new CiAiDependencyRegistrationReceiptIngestor(
                remoteReceiptStore, new FixedReceiptAuthenticator(authenticated: true));
            Assert(receiptIngestor.IngestAsync(acceptedReceipt.Payload, delivery, "TrentBest/Provider")
                .AsTask().GetAwaiter().GetResult() == CiAiDependencyRegistrationReceiptStoreOutcome.Recorded,
                "authenticated remote receipt was not ingested");
            Assert(receiptIngestor.IngestAsync(acceptedReceipt.Payload, delivery, "TrentBest/Provider")
                .AsTask().GetAwaiter().GetResult() == CiAiDependencyRegistrationReceiptStoreOutcome.Duplicate,
                "authenticated receipt retry was not deduplicated");
            var rejectingIngestor = new CiAiDependencyRegistrationReceiptIngestor(
                new FileCiAiDependencyRegistrationReceiptStore(Path.Combine(root, "rejected-receipts", "receipts.jsonl")),
                new FixedReceiptAuthenticator(authenticated: false));
            AssertThrows<InvalidDataException>(() =>
                rejectingIngestor.IngestAsync(acceptedReceipt.Payload, delivery, "TrentBest/Provider")
                    .AsTask().GetAwaiter().GetResult());
            AssertThrows<InvalidDataException>(() =>
                receiptIngestor.IngestAsync(acceptedReceipt.Payload, delivery, "TrentBest/PriorDependent")
                    .AsTask().GetAwaiter().GetResult());
            Assert(applied.Status == CiAiDependencyRegistrationApplyStatus.Applied, "initial snapshot was not applied");
            Assert(store.ApplyVerifiedSnapshot(CreateVerification(first, 42)).Status == CiAiDependencyRegistrationApplyStatus.Duplicate,
                "identical replay was not idempotent");

            var reopened = new FileCiAiDependencyRegistrationLedgerStore(root);
            var loaded = reopened.Load("TrentBest/Consumer");
            Assert(loaded is not null && loaded.SourceRevision == 42 &&
                loaded.Registrations.Single().TargetRepository == "TrentBest/Provider",
                "ledger did not survive store reconstruction");

            var stale = CreateSnapshot("1123456789abcdef0123456789abcdef01234567", "TrentBest/Stale");
            Assert(reopened.ApplyVerifiedSnapshot(CreateVerification(stale, 41)).Status == CiAiDependencyRegistrationApplyStatus.Stale,
                "older source run was not rejected");
            AssertThrows<ArgumentOutOfRangeException>(() =>
                CiAiDependencyRegistrationLedger.ApplyVerifiedSnapshot(null, stale, 0));

            var conflict = CreateSnapshot("2123456789abcdef0123456789abcdef01234567", "TrentBest/Conflict");
            Assert(reopened.ApplyVerifiedSnapshot(CreateVerification(conflict, 42)).Status == CiAiDependencyRegistrationApplyStatus.Conflict,
                "same-run changed snapshot was not rejected");

            var replacement = CreateSnapshot("3123456789abcdef0123456789abcdef01234567");
            Assert(reopened.ApplyVerifiedSnapshot(CreateVerification(replacement, 43)).Status == CiAiDependencyRegistrationApplyStatus.Applied,
                "newer complete empty snapshot was not applied");
            Assert(reopened.Load("TrentBest/Consumer")!.Registrations.Count == 0,
                "newer empty snapshot did not withdraw prior registrations");

            var other = CreateSnapshot("4123456789abcdef0123456789abcdef01234567", source: "TrentBest/Other");
            Assert(reopened.ApplyVerifiedSnapshot(CreateVerification(other, 7)).Status == CiAiDependencyRegistrationApplyStatus.Applied,
                "independent source ledger was not stored");
            Assert(reopened.ReadAll().Count == 2, "source ledgers were not enumerated independently");

            var corrupt = Directory.GetFiles(root, "*.json").OrderBy(path => path, StringComparer.Ordinal).First();
            var corruptJson = File.ReadAllText(corrupt);
            var alteredDigest = System.Text.RegularExpressions.Regex.Replace(corruptJson,
                "\"SnapshotSha256\": \"[0-9a-f]{64}\"",
                "\"SnapshotSha256\": \"" + new string('0', 64) + "\"");
            Assert(!string.Equals(corruptJson, alteredDigest, StringComparison.Ordinal),
                "corruption fixture did not locate the stored digest");
            File.WriteAllText(corrupt, alteredDigest);
            AssertThrows<InvalidDataException>(() => reopened.ReadAll());

            Console.WriteLine("PASS: 47 durable dependency federation, inventory, receiver, receipt authentication, retry, and GitHub transport checks.");
            return 47;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static CiAiDependencyRegistrationSnapshot CreateSnapshot(
        string sha, string? target = null, string source = "TrentBest/Consumer")
    {
        var builder = CiAi.ForRepository(source);
        if (target is not null) builder = builder.WithDependency(target);
        return CiAiDependencyRegistrationSnapshot.FromDefinition(builder.Build(), sha);
    }

    private static CiAiDependencyRegistrationSourceVerification CreateVerification(
        CiAiDependencyRegistrationSnapshot snapshot, long runId,
        string[]? allowedSourceRefs = null, bool trustedWorkflow = true,
        CiRunConclusion conclusion = CiRunConclusion.Success)
    {
        var sourceRef = "refs/heads/development";
        var provenanceJson = $$"""
        {
          "schema": "{{CiWorkflowProvenanceRecord.CurrentSchema}}",
          "repository": "{{snapshot.SourceRepository}}",
          "runId": {{runId}},
          "runAttempt": 1,
          "workflow": {
            "ref": "{{snapshot.SourceRepository}}/.github/workflows/ci.yml@{{sourceRef}}",
            "sha": "{{WorkflowSha}}"
          },
          "event": {
            "name": "push",
            "ref": "{{sourceRef}}",
            "sha": "{{snapshot.SourceCommitSha}}",
            "pullRequest": null
          },
          "checkout": {
            "ref": "{{sourceRef}}",
            "sha": "{{snapshot.SourceCommitSha}}"
          }
        }
        """;
        var bytes = Encoding.UTF8.GetBytes(provenanceJson);
        var provenance = CiWorkflowProvenanceRecord.ParseAndValidate(bytes, snapshot.SourceRepository,
            runId, 1, Convert.ToHexString(SHA256.HashData(bytes)));
        var policy = new CiWorkflowDefinitionTrustPolicy(snapshot.SourceRepository,
            ".github/workflows/ci.yml", sourceRef, WorkflowSha);
        var workflowBytes = Encoding.UTF8.GetBytes("name: trusted source CI");
        var approvedWorkflowBytes = trustedWorkflow
            ? workflowBytes
            : Encoding.UTF8.GetBytes("different protected workflow content");
        var trustDecision = policy.Evaluate(provenance, workflowBytes, approvedWorkflowBytes);

        var job = new CiWorkflowJobObservation(100, runId, 1, "build-and-test", "completed", "success",
            new Uri($"https://github.com/{snapshot.SourceRepository}/actions/runs/{runId}/job/100"),
            Array.AsReadOnly(new[]
            {
                new CiWorkflowStepObservation(1, "Build and test", "completed", "success", "2026-11-28T00:00:00Z")
            }))
        {
            StartedAt = DateTimeOffset.Parse("2026-11-28T00:00:00Z"),
            CompletedAt = DateTimeOffset.Parse("2026-11-28T00:01:00Z")
        };
        var observedRun = new CiWorkflowRunEvidenceObservation(snapshot.SourceRepository,
            ".github/workflows/ci.yml", runId, 1, sourceRef, snapshot.SourceCommitSha,
            "push", "CI", CiRunStatus.Completed, conclusion,
            new Uri($"https://github.com/{snapshot.SourceRepository}/actions/runs/{runId}"),
            DateTimeOffset.Parse("2026-11-28T00:02:00Z"),
            Array.AsReadOnly(new[] { job }), true, true);
        var packet = CiWorkflowEvidencePacket.FromValidatedProvenance(observedRun, provenance, trustDecision);
        var packetV2 = CiWorkflowEvidencePacketV2.FromPacket(packet,
            new[] { new CiWorkflowRequiredCheckBinding("build-and-test", "build-and-test") });
        return CiAiDependencyRegistrationSourceVerification.Verify(snapshot, packetV2, trustDecision,
            allowedSourceRefs ?? new[] { sourceRef });
    }

    private sealed class FixedReceiptAuthenticator(bool authenticated)
        : ICiAiDependencyRegistrationReceiptAuthenticator
    {
        public ValueTask<bool> AuthenticateAsync(CiAiDependencyRegistrationReceipt receipt,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(authenticated);
    }

    private sealed class FixedInstallationInventoryTokenProvider : ICiAiGitHubInstallationInventoryTokenProvider
    {
        public ValueTask<string> GetInstallationTokenAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult("inventory-token");
    }

    private sealed class FixedInventoryRevisionProvider(long revision) : ICiAiRepositoryInventoryRevisionProvider
    {
        public ValueTask<long> GetNextRevisionAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(revision);
    }

    private sealed class FixedInstallationTokenProvider : ICiAiGitHubInstallationTokenProvider
    {
        public ValueTask<string> GetInstallationTokenAsync(string targetRepository,
            CancellationToken cancellationToken = default) => ValueTask.FromResult("installation-token");
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, System.Net.Http.HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private sealed class FixedDependencyTransport : ICiAiDependencyRegistrationTransport
    {
        public ValueTask<CiAiDependencyRegistrationTransportResult> DispatchAsync(
            CiAiDependencyRegistrationDelivery delivery, string targetRepository,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new CiAiDependencyRegistrationTransportResult(
                CiAiDependencyRegistrationTransportDisposition.Queued, 204, "github.event-queued"));
    }

    private sealed class FixedDeliveryVerifier(
        CiAiDependencyRegistrationSourceVerification verification)
        : ICiAiDependencyRegistrationDeliveryVerifier
    {
        public ValueTask<CiAiDependencyRegistrationSourceVerification> VerifyAsync(
            CiAiDependencyRegistrationDelivery delivery, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(verification);
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
