namespace TheSingularityWorkshop.CiAi;

/// <summary>One notice delivery attempt and its durable local receipt outcome.</summary>
public sealed record FoundryArtifactReadyNoticeDeliveryAttempt(
    string NoticeId,
    FoundryArtifactIngressOutcome Outcome,
    FoundryArtifactIngressReceiptRecordOutcome? ReceiptRecordOutcome,
    string? ErrorCode,
    TimeSpan? RetryAfter);

/// <summary>Summary of one bounded pass over the durable Foundry notice outbox.</summary>
public sealed record FoundryArtifactReadyNoticeDeliveryBatch(
    int PendingAtStart,
    int Attempted,
    int Acknowledged,
    int RemainingPending,
    IReadOnlyList<FoundryArtifactReadyNoticeDeliveryAttempt> Attempts);

/// <summary>
/// Performs one bounded, sequential delivery pass over the durable Foundry notice outbox.
/// Only authenticated Accepted/Duplicate receipts returned by the sender are persisted as acknowledgements.
/// </summary>
/// <remarks>
/// This coordinator does not schedule retries or timers. Transient outcomes preserve Retry-After and leave
/// notices pending for a later caller invocation. If receipt persistence fails after remote acceptance,
/// the notice remains pending and the next attempt reuses the same notice/idempotency identity.
/// </remarks>
public sealed class FoundryArtifactReadyNoticeDeliveryCoordinator
{
    private const int MaximumBatchSize = 100;
    private readonly FileFoundryArtifactReadyNoticeOutbox _outbox;
    private readonly FileFoundryArtifactIngressReceiptLedger _receiptLedger;
    private readonly FoundryArtifactReadyNoticeClient _client;

    public FoundryArtifactReadyNoticeDeliveryCoordinator(
        FileFoundryArtifactReadyNoticeOutbox outbox,
        FileFoundryArtifactIngressReceiptLedger receiptLedger,
        FoundryArtifactReadyNoticeClient client)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(receiptLedger);
        ArgumentNullException.ThrowIfNull(client);
        _outbox = outbox;
        _receiptLedger = receiptLedger;
        _client = client;
    }

    /// <summary>Attempts at most <paramref name="maximumDeliveries"/> pending notices once, in outbox order.</summary>
    public async Task<FoundryArtifactReadyNoticeDeliveryBatch> DeliverPendingAsync(
        int maximumDeliveries = 20,
        CancellationToken cancellationToken = default)
    {
        if (maximumDeliveries is < 1 or > MaximumBatchSize)
            throw new ArgumentOutOfRangeException(nameof(maximumDeliveries),
                $"Batch size must be between 1 and {MaximumBatchSize}.");

        var pending = _outbox.ReadPending(_receiptLedger.ReadAcknowledgedNoticeIds());
        var attempts = new List<FoundryArtifactReadyNoticeDeliveryAttempt>();
        var acknowledged = 0;

        foreach (var delivery in pending.Take(maximumDeliveries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _client.SendAsync(delivery, cancellationToken).ConfigureAwait(false);
            var receiptOutcome = (FoundryArtifactIngressReceiptRecordOutcome?)null;
            var outcome = result.Outcome;
            var errorCode = result.ErrorCode;

            if (result.IsAcknowledged)
            {
                if (result.Receipt is null)
                    throw new InvalidDataException("Acknowledged ingress result did not contain a durable receipt.");

                receiptOutcome = _receiptLedger.Record(result.Receipt, delivery);
                if (receiptOutcome is FoundryArtifactIngressReceiptRecordOutcome.Recorded
                    or FoundryArtifactIngressReceiptRecordOutcome.Duplicate)
                {
                    acknowledged++;
                }
                else
                {
                    outcome = FoundryArtifactIngressOutcome.Conflict;
                    errorCode = "receipt.ledger-conflict";
                }
            }

            attempts.Add(new FoundryArtifactReadyNoticeDeliveryAttempt(
                delivery.NoticeId, outcome, receiptOutcome, errorCode, result.RetryAfter));

            // These outcomes are global transport/authorization/back-pressure signals.
            // Leave all remaining notices pending for a later, policy-controlled invocation.
            if (outcome is FoundryArtifactIngressOutcome.AuthenticationRequired
                or FoundryArtifactIngressOutcome.AuthorizationDenied
                or FoundryArtifactIngressOutcome.RateLimited
                or FoundryArtifactIngressOutcome.RetryableFailure
                or FoundryArtifactIngressOutcome.Rejected)
                break;
        }

        var remaining = _outbox.ReadPending(_receiptLedger.ReadAcknowledgedNoticeIds()).Count;
        return new FoundryArtifactReadyNoticeDeliveryBatch(
            pending.Count, attempts.Count, acknowledged, remaining, Array.AsReadOnly(attempts.ToArray()));
    }
}
