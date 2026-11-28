using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Durable retry state for one delivery/recipient pair.</summary>
public sealed record CiAiDependencyRegistrationDispatchState(
    string DeliveryId,
    string TargetRepository,
    int AttemptCount,
    DateTimeOffset? NextAttemptAtUtc,
    CiAiDependencyRegistrationTransportDisposition LastDisposition,
    string LastOutcomeCode,
    bool Terminal);

/// <summary>
/// File-backed retry schedule. Backoff is deterministic exponential (30 seconds to one hour).
/// A permanent transport rejection is terminal and requires operator/configuration intervention.
/// </summary>
public sealed class FileCiAiDependencyRegistrationDispatchStateStore : ICiAiDependencyRegistrationDispatchStateStore
{
    private readonly string _directory;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public FileCiAiDependencyRegistrationDispatchStateStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    public CiAiDependencyRegistrationDispatchState? Load(string deliveryId, string targetRepository)
    {
        var path = GetPath(deliveryId, targetRepository);
        if (!File.Exists(path)) return null;
        using var fileLock = AcquireLock(path);
        return Read(path);
    }

    public CiAiDependencyRegistrationDispatchState RecordAttempt(string deliveryId, string targetRepository,
        CiAiDependencyRegistrationTransportResult result, DateTimeOffset attemptedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(result);
        ValidateIdentity(deliveryId, targetRepository);
        if (attemptedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Dispatch attempt time must be UTC.", nameof(attemptedAtUtc));
        if (!Enum.IsDefined(result.Disposition) || string.IsNullOrWhiteSpace(result.OutcomeCode))
            throw new ArgumentException("Dispatch result is invalid.", nameof(result));

        var path = GetPath(deliveryId, targetRepository);
        using var fileLock = AcquireLock(path);
        var current = File.Exists(path) ? Read(path) : null;
        if (current?.Terminal == true)
            throw new InvalidOperationException("A terminal dispatch state cannot be retried without explicit operator reset.");

        var attempt = (current?.AttemptCount ?? 0) + 1;
        var terminal = result.Disposition == CiAiDependencyRegistrationTransportDisposition.PermanentFailure;
        var exponent = Math.Min(attempt - 1, 10);
        var delaySeconds = Math.Min(3600d, 30d * Math.Pow(2d, exponent));
        DateTimeOffset? nextAttempt = terminal ? null : attemptedAtUtc.AddSeconds(delaySeconds);
        var next = new CiAiDependencyRegistrationDispatchState(deliveryId, targetRepository,
            attempt, nextAttempt, result.Disposition, result.OutcomeCode, terminal);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, next, JsonOptions);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
        return next;
    }

    private FileStream AcquireLock(string path)
    {
        Directory.CreateDirectory(_directory);
        return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private string GetPath(string deliveryId, string targetRepository)
    {
        ValidateIdentity(deliveryId, targetRepository);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            deliveryId + "|" + targetRepository.ToUpperInvariant()))).ToLowerInvariant();
        return Path.Combine(_directory, key + ".json");
    }

    private static void ValidateIdentity(string deliveryId, string targetRepository)
    {
        if (string.IsNullOrWhiteSpace(deliveryId) || !deliveryId.StartsWith("depdelivery-", StringComparison.Ordinal))
            throw new ArgumentException("Delivery identity is invalid.", nameof(deliveryId));
        if (!RepositoryIdentity.IsValid(targetRepository))
            throw new ArgumentException("Target repository must be canonical owner/repository.", nameof(targetRepository));
    }

    private static CiAiDependencyRegistrationDispatchState Read(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var state = JsonSerializer.Deserialize<CiAiDependencyRegistrationDispatchState>(input)
            ?? throw new InvalidDataException("Dispatch state is empty.");
        ValidateIdentity(state.DeliveryId, state.TargetRepository);
        if (state.AttemptCount < 1 || !Enum.IsDefined(state.LastDisposition) ||
            string.IsNullOrWhiteSpace(state.LastOutcomeCode) ||
            (state.Terminal != (state.LastDisposition == CiAiDependencyRegistrationTransportDisposition.PermanentFailure)) ||
            (state.Terminal && state.NextAttemptAtUtc is not null) ||
            (!state.Terminal && state.NextAttemptAtUtc is null) ||
            (state.NextAttemptAtUtc is { Offset: var offset } && offset != TimeSpan.Zero))
            throw new InvalidDataException("Dispatch state is invalid.");
        return state;
    }
}

/// <summary>
/// Sends due outbox deliveries to recipients that have not yet issued any receipt. Queued events
/// remain outstanding and are retried with backoff until an authenticated receipt is recorded.
/// </summary>
public sealed class CiAiDependencyRegistrationDispatchCoordinator
{
    private readonly ICiAiDependencyRegistrationOutboxStore _outbox;
    private readonly ICiAiDependencyRegistrationReceiptStore _receipts;
    private readonly ICiAiDependencyRegistrationDispatchStateStore _states;
    private readonly ICiAiDependencyRegistrationTransport _transport;

    public CiAiDependencyRegistrationDispatchCoordinator(
        ICiAiDependencyRegistrationOutboxStore outbox,
        ICiAiDependencyRegistrationReceiptStore receipts,
        ICiAiDependencyRegistrationDispatchStateStore states,
        ICiAiDependencyRegistrationTransport transport)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _receipts = receipts ?? throw new ArgumentNullException(nameof(receipts));
        _states = states ?? throw new ArgumentNullException(nameof(states));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async ValueTask<IReadOnlyList<CiAiDependencyRegistrationDispatchAttempt>> DispatchDueAsync(
        DateTimeOffset nowUtc, int maximumDispatches = 100,
        CancellationToken cancellationToken = default)
    {
        if (nowUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Coordinator time must be UTC.", nameof(nowUtc));
        if (maximumDispatches < 1) throw new ArgumentOutOfRangeException(nameof(maximumDispatches));

        var knownReceipts = _receipts.ReadAll();
        var attempts = new List<CiAiDependencyRegistrationDispatchAttempt>();
        foreach (var delivery in _outbox.ReadAll())
        {
            foreach (var target in delivery.RecipientRepositories.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (knownReceipts.Any(receipt => receipt.DeliveryId == delivery.DeliveryId &&
                    string.Equals(receipt.TargetRepository, target, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var state = _states.Load(delivery.DeliveryId, target);
                if (state?.Terminal == true ||
                    (state?.NextAttemptAtUtc is { } due && due > nowUtc))
                    continue;

                var result = await _transport.DispatchAsync(delivery, target, cancellationToken)
                    .ConfigureAwait(false);
                var recorded = _states.RecordAttempt(delivery.DeliveryId, target, result, nowUtc);
                attempts.Add(new CiAiDependencyRegistrationDispatchAttempt(delivery.DeliveryId,
                    target, recorded.AttemptCount, result));
                if (attempts.Count >= maximumDispatches)
                    return Array.AsReadOnly(attempts.ToArray());
            }
        }
        return Array.AsReadOnly(attempts.ToArray());
    }
}

public sealed record CiAiDependencyRegistrationDispatchAttempt(
    string DeliveryId,
    string TargetRepository,
    int AttemptCount,
    CiAiDependencyRegistrationTransportResult TransportResult);
