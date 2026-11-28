namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Authenticates that a receipt was issued by the named recipient repository. Implementations
/// must verify a trusted provider identity (for example, a protected workflow identity or signed
/// service identity); parsing the receipt JSON is not authentication.
/// </summary>
public interface ICiAiDependencyRegistrationReceiptAuthenticator
{
    ValueTask<bool> AuthenticateAsync(CiAiDependencyRegistrationReceipt receipt,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Validates delivery correlation and authenticates a remote receipt before it enters the durable
/// receipt store used by dispatch coordination.
/// </summary>
public sealed class CiAiDependencyRegistrationReceiptIngestor
{
    private readonly ICiAiDependencyRegistrationReceiptStore _store;
    private readonly ICiAiDependencyRegistrationReceiptAuthenticator _authenticator;

    public CiAiDependencyRegistrationReceiptIngestor(
        ICiAiDependencyRegistrationReceiptStore store,
        ICiAiDependencyRegistrationReceiptAuthenticator authenticator)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
    }

    public async ValueTask<CiAiDependencyRegistrationReceiptStoreOutcome> IngestAsync(
        string receiptJson, CiAiDependencyRegistrationDelivery expectedDelivery,
        string expectedTargetRepository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedDelivery);
        if (!RepositoryIdentity.IsValid(expectedTargetRepository) ||
            !expectedDelivery.RecipientRepositories.Contains(expectedTargetRepository,
                StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Expected receipt target must be an explicit delivery recipient.",
                nameof(expectedTargetRepository));

        var receipt = CiAiDependencyRegistrationReceipt.FromJson(receiptJson);
        if (!string.Equals(receipt.DeliveryId, expectedDelivery.DeliveryId, StringComparison.Ordinal) ||
            !string.Equals(receipt.TargetRepository, expectedTargetRepository, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.SourceRepository, expectedDelivery.SourceRepository, StringComparison.OrdinalIgnoreCase) ||
            receipt.SourceRevision != expectedDelivery.SourceRevision ||
            !string.Equals(receipt.SourceCommitSha, expectedDelivery.SourceCommitSha, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.DeliveryPayloadSha256, expectedDelivery.PayloadSha256, StringComparison.Ordinal))
            throw new InvalidDataException("Remote receipt does not correlate to the expected delivery and recipient.");

        if (!await _authenticator.AuthenticateAsync(receipt, cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("Remote receipt recipient identity could not be authenticated.");

        var outcome = _store.Record(receipt);
        if (outcome == CiAiDependencyRegistrationReceiptStoreOutcome.Conflict)
            throw new InvalidDataException("A contradictory receipt already exists for this delivery and recipient.");
        return outcome;
    }
}
