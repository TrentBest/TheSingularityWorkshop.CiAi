using TheSingularityWorkshop.CiAi;

internal static class AwaitGateEventOutboxTests
{
    private const string Consumer = "TrentBest/The";
    private const string Provider = "TrentBest/MyVR";
    private const string SourceSha = "abcdef0123456789abcdef0123456789abcdef01";
    private const string ManifestDigest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string EvidenceDigest = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    public static int Run()
    {
        var passed = 0;
        var root = Path.Combine(Path.GetTempPath(), "ciai-await-gate-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var contract = new AwaitGateContract("B", 1, Consumer, Provider, SourceSha,
                ManifestDigest, "policy-v1",
                new[] { new AwaitGateRequirement("dimensions", "Dimensions validated", "valid",
                    AwaitGateEvidenceKind.Machine) });
            var evidence = new AwaitGateEvidence("dimensions", "B", 1, contract.ContractDigest, Provider,
                SourceSha, ManifestDigest, EvidenceDigest, AwaitGateEvidenceKind.Machine, "valid",
                "workflow-artifact:42:1", "42", 1);
            var evaluation = AwaitGateEvaluator.Evaluate(contract, new[] { evidence });
            var evt = new AwaitGateEvent("event-1", "await-1", Consumer, AwaitGateEventKind.Satisfied,
                evaluation, DateTimeOffset.Parse("2026-10-31T17:00:00Z"));

            Check("event outbox persists and reloads exact source-bound event", () =>
            {
                var outbox = new FileAwaitGateEventOutbox(Path.Combine(root, "outbox"));
                Assert(outbox.Enqueue(evt) == AwaitGateEventStoreOutcome.Saved, "event was not saved");
                var reopened = new FileAwaitGateEventOutbox(Path.Combine(root, "outbox"));
                var loaded = reopened.Load("event-1");
                Assert(loaded is not null && loaded.PayloadDigest == evt.PayloadDigest &&
                    loaded.ProviderSourceSha == SourceSha && loaded.ConsumerRepository == Consumer,
                    "reloaded event did not preserve its exact identity");
            });

            Check("event outbox treats identical retries as idempotent", () =>
            {
                var outbox = new FileAwaitGateEventOutbox(Path.Combine(root, "outbox"));
                Assert(outbox.Enqueue(evt) == AwaitGateEventStoreOutcome.AlreadyExists,
                    "identical event retry was not recognized");
            });

            Check("event outbox rejects conflicting reuse of an event ID", () =>
            {
                var changedContract = new AwaitGateContract("B", 1, Consumer, Provider, SourceSha,
                    ManifestDigest, "policy-v2",
                    new[] { new AwaitGateRequirement("dimensions", "Dimensions validated", "valid",
                        AwaitGateEvidenceKind.Machine) });
                var changedEvidence = new AwaitGateEvidence("dimensions", "B", 1, changedContract.ContractDigest,
                    Provider, SourceSha, ManifestDigest, EvidenceDigest, AwaitGateEvidenceKind.Machine, "valid",
                    "workflow-artifact:42:1", "42", 1);
                var changedEvaluation = AwaitGateEvaluator.Evaluate(changedContract, new[] { changedEvidence });
                var conflict = new AwaitGateEvent("event-1", "await-1", Consumer,
                    AwaitGateEventKind.Satisfied, changedEvaluation,
                    DateTimeOffset.Parse("2026-10-31T17:00:00Z"));
                Assert(new FileAwaitGateEventOutbox(Path.Combine(root, "outbox")).Enqueue(conflict) ==
                    AwaitGateEventStoreOutcome.Conflict, "event ID was overwritten by a different payload");
            });

            Check("outbox enumerates events deterministically for host delivery", () =>
            {
                var invalidEvidence = new AwaitGateEvidence("dimensions", "B", 1, contract.ContractDigest, Provider,
                    SourceSha, ManifestDigest, EvidenceDigest, AwaitGateEvidenceKind.Machine, "invalid",
                    "workflow-artifact:42:1", "42", 1);
                var invalidEvaluation = AwaitGateEvaluator.Evaluate(contract, new[] { invalidEvidence });
                var invalidated = new AwaitGateEvent("event-2", "await-1", Consumer,
                    AwaitGateEventKind.Invalidated, invalidEvaluation,
                    DateTimeOffset.Parse("2026-10-31T17:01:00Z"));
                var outbox = new FileAwaitGateEventOutbox(Path.Combine(root, "outbox"));
                Assert(outbox.Enqueue(invalidated) == AwaitGateEventStoreOutcome.Saved,
                    "invalidation event was not saved");
                var events = outbox.ReadAll();
                Assert(events.Count == 2 && events[0].EventId == "event-1" && events[1].EventId == "event-2",
                    "event enumeration was not stable");
            });

            Check("satisfied event requires satisfied evaluation", () =>
                Throws<ArgumentException>(() => new AwaitGateEvent("bad", "await-1", Consumer,
                    AwaitGateEventKind.Satisfied,
                    AwaitGateEvaluator.Evaluate(contract, Array.Empty<AwaitGateEvidence>()),
                    DateTimeOffset.UtcNow)));

            Check("invalidation event records revocation without rewriting satisfaction", () =>
            {
                var rejectedEvidence = new AwaitGateEvidence("dimensions", "B", 1, contract.ContractDigest, Provider,
                    SourceSha, ManifestDigest, EvidenceDigest, AwaitGateEvidenceKind.Machine, "invalid",
                    "workflow-artifact:42:1", "42", 1);
                var rejected = AwaitGateEvaluator.Evaluate(contract, new[] { rejectedEvidence });
                var invalidated = new AwaitGateEvent("event-revoked", "await-1", Consumer,
                    AwaitGateEventKind.Invalidated, rejected, DateTimeOffset.UtcNow);
                Assert(invalidated.Kind == AwaitGateEventKind.Invalidated &&
                    invalidated.EvaluationState == AwaitGateState.Rejected,
                    "revocation/rejection did not produce a distinct invalidation event");
            });

            Check("receipt ledger accepts exact authenticated-host receipt and suppresses replay", () =>
            {
                var ledger = new FileAwaitGateEventReceiptLedger(Path.Combine(root, "receipts"));
                var receipt = new AwaitGateDeliveryReceipt("receipt-1", evt.EventId, evt.PayloadDigest,
                    Consumer, "foundry-host-prod", AwaitGateReceiptOutcome.Accepted,
                    DateTimeOffset.Parse("2026-10-31T17:02:00Z"));
                Assert(ledger.Record(receipt, evt) == AwaitGateEventStoreOutcome.Saved,
                    "accepted receipt was not saved");
                Assert(new FileAwaitGateEventReceiptLedger(Path.Combine(root, "receipts"))
                    .HasAcceptedReceipt(evt.EventId, evt.PayloadDigest),
                    "accepted receipt did not suppress replay after reopening");
                Assert(ledger.Record(receipt, evt) == AwaitGateEventStoreOutcome.AlreadyExists,
                    "identical receipt retry was not idempotent");
            });

            Check("rejected receipt never suppresses delivery", () =>
            {
                var ledger = new FileAwaitGateEventReceiptLedger(Path.Combine(root, "rejected"));
                var receipt = new AwaitGateDeliveryReceipt("receipt-rejected", evt.EventId, evt.PayloadDigest,
                    Consumer, "foundry-host-prod", AwaitGateReceiptOutcome.Rejected, DateTimeOffset.UtcNow);
                Assert(ledger.Record(receipt, evt) == AwaitGateEventStoreOutcome.Conflict &&
                    !ledger.HasAcceptedReceipt(evt.EventId, evt.PayloadDigest),
                    "rejected receipt suppressed replay");
            });

            Check("receipt rejects a mismatched event digest or consumer", () =>
            {
                var ledger = new FileAwaitGateEventReceiptLedger(Path.Combine(root, "mismatch"));
                var wrongDigest = new AwaitGateDeliveryReceipt("receipt-wrong-digest", evt.EventId,
                    new string('d', 64), Consumer, "foundry-host-prod", AwaitGateReceiptOutcome.Accepted,
                    DateTimeOffset.UtcNow);
                var wrongConsumer = new AwaitGateDeliveryReceipt("receipt-wrong-consumer", evt.EventId,
                    evt.PayloadDigest, "TrentBest/Other", "foundry-host-prod", AwaitGateReceiptOutcome.Accepted,
                    DateTimeOffset.UtcNow);
                Assert(ledger.Record(wrongDigest, evt) == AwaitGateEventStoreOutcome.Conflict &&
                    ledger.Record(wrongConsumer, evt) == AwaitGateEventStoreOutcome.Conflict,
                    "mismatched receipt was recorded");
            });
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS Await/Gate outbox {passed}: {name}");
        }
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
