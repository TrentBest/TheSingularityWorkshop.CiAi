namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Receiver-side trust boundary. Implementations retrieve source evidence from an authenticated
/// provider boundary and independently verify the source workflow before returning a verification.
/// </summary>
public interface ICiAiDependencyRegistrationDeliveryVerifier
{
    ValueTask<CiAiDependencyRegistrationSourceVerification> VerifyAsync(
        CiAiDependencyRegistrationDelivery delivery, CancellationToken cancellationToken = default);
}

public sealed record CiAiDependencyRegistrationReceiveResult(
    CiAiDependencyRegistrationReceipt Receipt,
    CiAiDependencyRegistrationReceiptStoreOutcome ReceiptStoreOutcome,
    CiAiDependencyRegistrationApplyStatus ApplyStatus);

/// <summary>
/// Applies one incoming delivery for this repository. Ledger persistence precedes receipt persistence,
/// so a retry after a crash is idempotently reconciled before the acknowledgement is recorded.
/// </summary>
public sealed class CiAiDependencyRegistrationReceiver
{
    private readonly string _targetRepository;
    private readonly ICiAiDependencyRegistrationLedgerStore _ledgerStore;
    private readonly ICiAiDependencyRegistrationReceiptStore _receiptStore;
    private readonly ICiAiDependencyRegistrationDeliveryVerifier _verifier;

    public CiAiDependencyRegistrationReceiver(string targetRepository,
        ICiAiDependencyRegistrationLedgerStore ledgerStore,
        ICiAiDependencyRegistrationReceiptStore receiptStore,
        ICiAiDependencyRegistrationDeliveryVerifier verifier)
    {
        if (!RepositoryIdentity.IsValid(targetRepository))
            throw new ArgumentException("Receiver repository must be canonical owner/repository.", nameof(targetRepository));
        _targetRepository = targetRepository;
        _ledgerStore = ledgerStore ?? throw new ArgumentNullException(nameof(ledgerStore));
        _receiptStore = receiptStore ?? throw new ArgumentNullException(nameof(receiptStore));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
    }

    public async ValueTask<CiAiDependencyRegistrationReceiveResult> ReceiveAsync(
        string deliveryJson, CancellationToken cancellationToken = default)
    {
        var delivery = CiAiDependencyRegistrationDelivery.FromJson(deliveryJson);
        if (!delivery.RecipientRepositories.Contains(_targetRepository, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Incoming dependency delivery does not name this repository as a recipient.");

        var verification = await _verifier.VerifyAsync(delivery, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Delivery verifier returned no verified source evidence.");
        if (!string.Equals(verification.Snapshot.ToJson(), delivery.Snapshot.ToJson(), StringComparison.Ordinal) ||
            verification.SourceRevision != delivery.SourceRevision ||
            verification.SourceRunAttempt != delivery.SourceRunAttempt ||
            !string.Equals(verification.SourceCommitSha, delivery.SourceCommitSha, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(verification.EvidencePayloadSha256, delivery.EvidencePayloadSha256, StringComparison.Ordinal) ||
            !string.Equals(verification.WorkflowPath, delivery.WorkflowPath, StringComparison.Ordinal) ||
            !string.Equals(verification.SourceRef, delivery.SourceRef, StringComparison.Ordinal))
            throw new InvalidDataException("Independently verified source evidence does not match the delivery envelope.");

        var applied = _ledgerStore.ApplyVerifiedSnapshot(verification);
        var receipt = CiAiDependencyRegistrationReceipt.FromApplyResult(delivery, _targetRepository, applied);
        var recorded = _receiptStore.Record(receipt);
        if (recorded == CiAiDependencyRegistrationReceiptStoreOutcome.Conflict)
            throw new InvalidDataException("A different receipt already exists for this delivery and receiver.");
        return new CiAiDependencyRegistrationReceiveResult(receipt, recorded, applied.Status);
    }
}
