namespace TheSingularityWorkshop.CiAi;

/// <summary>Persistence contract for one latest complete verified source ledger per repository.</summary>
public interface ICiAiDependencyRegistrationLedgerStore
{
    CiAiDependencyRegistrationApplyResult ApplyVerifiedSnapshot(
        CiAiDependencyRegistrationSourceVerification verification);
    CiAiDependencyRegistrationLedger? Load(string sourceRepository);
    IReadOnlyList<CiAiDependencyRegistrationLedger> ReadAll();
}

/// <summary>Persistence contract for immutable receiver acknowledgements.</summary>
public interface ICiAiDependencyRegistrationReceiptStore
{
    CiAiDependencyRegistrationReceiptStoreOutcome Record(CiAiDependencyRegistrationReceipt receipt);
    IReadOnlyList<CiAiDependencyRegistrationReceipt> ReadAll();
}

/// <summary>Persistence contract for immutable verified source deliveries.</summary>
public interface ICiAiDependencyRegistrationOutboxStore
{
    CiAiDependencyRegistrationOutboxEnqueueOutcome Enqueue(CiAiDependencyRegistrationDelivery delivery);
    IReadOnlyList<CiAiDependencyRegistrationDelivery> ReadAll();
}

/// <summary>Persistence contract for per-recipient dispatch retry state.</summary>
public interface ICiAiDependencyRegistrationDispatchStateStore
{
    CiAiDependencyRegistrationDispatchState? Load(string deliveryId, string targetRepository);
    CiAiDependencyRegistrationDispatchState RecordAttempt(string deliveryId, string targetRepository,
        CiAiDependencyRegistrationTransportResult result, DateTimeOffset attemptedAtUtc);
}
