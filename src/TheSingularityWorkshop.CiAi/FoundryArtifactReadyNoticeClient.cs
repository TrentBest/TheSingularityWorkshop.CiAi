using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Obtains a short-lived Foundry ingress token from host-managed identity or secret storage.</summary>
/// <remarks>Implementations must never expose token values through logs, diagnostics, or model-visible data.</remarks>
public interface IFoundryIngressCredentialProvider
{
    ValueTask<string> GetBearerTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>One validated durable response from Foundry's v1 artifact-ready ingress.</summary>
public sealed record FoundryArtifactIngressReceipt(
    string Contract,
    string ReceiptId,
    string NoticeId,
    string IdempotencyKey,
    string Outcome,
    DateTimeOffset AcceptedAt);

public enum FoundryArtifactIngressOutcome
{
    Accepted,
    Duplicate,
    AuthenticationRequired,
    AuthorizationDenied,
    Conflict,
    InvalidRequest,
    RateLimited,
    RetryableFailure,
    Rejected
}

/// <summary>Transport result. Only Accepted and Duplicate represent durable receiver acknowledgement.</summary>
public sealed record FoundryArtifactIngressResult(
    FoundryArtifactIngressOutcome Outcome,
    HttpStatusCode? StatusCode = null,
    FoundryArtifactIngressReceipt? Receipt = null,
    string? ErrorCode = null,
    TimeSpan? RetryAfter = null)
{
    public bool IsAcknowledged =>
        Outcome is FoundryArtifactIngressOutcome.Accepted or FoundryArtifactIngressOutcome.Duplicate;
}

/// <summary>
/// Sends metadata-only ArtifactReadyNotice v1 messages to the Foundry-owned HTTPS ingress.
/// The caller retains and retries the same notice after transient failures; this client never
/// invents a new notice or idempotency identity and never transfers artifact bytes.
/// </summary>
/// <remarks>
/// The supplied HttpClient must be configured with automatic redirects disabled. The sender rejects
/// redirect responses, but cannot prevent an injected handler from following a redirect before the
/// response reaches this class. Credentials are requested for each attempt and sent only as a bearer
/// Authorization header. Retry scheduling and durable notice persistence remain host responsibilities.
/// </remarks>
public sealed class FoundryArtifactReadyNoticeClient
{
    public const string Contract = "foundry.ciai-artifact-ingress.v1";
    public const string EndpointPath = "/api/v1/integrations/ciai/artifact-ready";
    public const int MaximumNoticeBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly IFoundryIngressCredentialProvider _credentials;

    public FoundryArtifactReadyNoticeClient(
        HttpClient httpClient,
        Uri endpoint,
        IFoundryIngressCredentialProvider credentials)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(credentials);
        if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !endpoint.IsDefaultPort || !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment) ||
            !endpoint.AbsolutePath.EndsWith(EndpointPath, StringComparison.Ordinal))
            throw new ArgumentException("Foundry ingress endpoint must be an HTTPS URL on the default port with the exact v1 route and no credentials, query, or fragment.", nameof(endpoint));

        _httpClient = httpClient;
        _endpoint = endpoint;
        _credentials = credentials;
    }

    /// <summary>Sends one immutable notice attempt and classifies the receiver's response.</summary>
    public async Task<FoundryArtifactIngressResult> SendAsync(
        ArtifactReadyNotice notice,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notice);
        if (notice.ContractVersion != ArtifactReadyNotice.SupportedContractVersion)
            throw new ArgumentException("Only ArtifactReadyNotice v1 is supported.", nameof(notice));

        var payload = JsonSerializer.SerializeToUtf8Bytes(notice, JsonOptions);
        if (payload.Length > MaximumNoticeBytes)
            return new FoundryArtifactIngressResult(FoundryArtifactIngressOutcome.InvalidRequest,
                ErrorCode: "notice.exceeds-64-kib-limit");

        string token;
        try
        {
            token = await _credentials.GetBearerTokenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new FoundryArtifactIngressResult(FoundryArtifactIngressOutcome.AuthenticationRequired,
                ErrorCode: "credential.unavailable");
        }

        if (string.IsNullOrWhiteSpace(token) || token.Length > 8192 || token.Any(char.IsControl))
            return new FoundryArtifactIngressResult(FoundryArtifactIngressOutcome.AuthenticationRequired,
                ErrorCode: "credential.invalid");

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new ByteArrayContent(payload);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException)
        {
            return new FoundryArtifactIngressResult(FoundryArtifactIngressOutcome.RetryableFailure,
                ErrorCode: "transport.unavailable");
        }

        using (response)
        {
            var status = response.StatusCode;
            if (status is HttpStatusCode.Accepted or HttpStatusCode.OK)
                return await ReadReceiptAsync(response, notice, cancellationToken).ConfigureAwait(false);

            if ((int)status is >= 300 and < 400)
                return new FoundryArtifactIngressResult(FoundryArtifactIngressOutcome.Rejected, status,
                    ErrorCode: "receiver.redirect-rejected");
            if (status == HttpStatusCode.Unauthorized)
                return new FoundryArtifactIngressResult(FoundryArtifactIngressOutcome.AuthenticationRequired, status,
                    ErrorCode: "auth.required");
            if (status == HttpStatusCode.Forbidden)
                return new FoundryArtifactIngressResult(FoundryArtifactIngressOutcome.AuthorizationDenied, status,
                    ErrorCode: "auth.denied");
            if (status == HttpStatusCode.Conflict)
                return new FoundryArtifactIngressResult(FoundryArtifactIngressOutcome.Conflict, status,
                    ErrorCode: "notice.idempotency-conflict");
            if (status == HttpStatusCode.TooManyRequests)
                return new FoundryArtifactIngressResult(FoundryArtifactIngressOutcome.RateLimited, status,
                    ErrorCode: "receiver.rate-limited", RetryAfter: GetRetryAfter(response));
            if (status == HttpStatusCode.BadRequest || (int)status == 413 ||
                status == HttpStatusCode.UnsupportedMediaType || (int)status == 422)
                return new FoundryArtifactIngressResult(FoundryArtifactIngressOutcome.InvalidRequest, status,
                    ErrorCode: await ReadSafeErrorCodeAsync(response, cancellationToken).ConfigureAwait(false));
            if ((int)status >= 500)
                return new FoundryArtifactIngressResult(FoundryArtifactIngressOutcome.RetryableFailure, status,
                    ErrorCode: "receiver.unavailable");

            return new FoundryArtifactIngressResult(FoundryArtifactIngressOutcome.Rejected, status,
                ErrorCode: "receiver.rejected");
        }
    }

    private static async Task<FoundryArtifactIngressResult> ReadReceiptAsync(
        HttpResponseMessage response,
        ArtifactReadyNotice notice,
        CancellationToken cancellationToken)
    {
        var bytes = await ReadBoundedContentAsync(response.Content, MaximumNoticeBytes, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
            return InvalidReceipt(response.StatusCode, "receipt.exceeds-response-limit");

        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            var contract = RequiredString(root, "contract");
            var receiptId = RequiredString(root, "receiptId");
            var noticeId = RequiredString(root, "noticeId");
            var idempotencyKey = RequiredString(root, "idempotencyKey");
            var outcome = RequiredString(root, "outcome");
            var acceptedAtText = RequiredString(root, "acceptedAt");
            if (!string.Equals(contract, Contract, StringComparison.Ordinal) ||
                !string.Equals(noticeId, notice.NoticeId, StringComparison.Ordinal) ||
                !string.Equals(idempotencyKey, notice.Manifest.IdempotencyKey, StringComparison.Ordinal) ||
                !DateTimeOffset.TryParse(acceptedAtText, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var acceptedAt))
                return InvalidReceipt(response.StatusCode, "receipt.provenance-mismatch");

            var expectedOutcome = response.StatusCode == HttpStatusCode.Accepted ? "accepted" : "duplicate";
            if (!string.Equals(outcome, expectedOutcome, StringComparison.Ordinal))
                return InvalidReceipt(response.StatusCode, "receipt.outcome-mismatch");

            var receipt = new FoundryArtifactIngressReceipt(contract, receiptId, noticeId,
                idempotencyKey, outcome, acceptedAt);
            return new FoundryArtifactIngressResult(
                outcome == "accepted" ? FoundryArtifactIngressOutcome.Accepted : FoundryArtifactIngressOutcome.Duplicate,
                response.StatusCode, receipt);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return InvalidReceipt(response.StatusCode, "receipt.malformed");
        }
    }

    private static string RequiredString(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException("Required receipt field is missing.");
        return value.GetString()!;
    }

    private static async Task<string> ReadSafeErrorCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await ReadBoundedContentAsync(response.Content, 8192, cancellationToken).ConfigureAwait(false);
            if (bytes is null) return "receiver.invalid-request";
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.TryGetProperty("code", out var code) &&
                code.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(code.GetString()) &&
                code.GetString()!.Length <= 200 &&
                !code.GetString()!.Any(char.IsControl)
                ? code.GetString()!
                : "receiver.invalid-request";
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return "receiver.invalid-request";
        }
    }


    private static async Task<byte[]?> ReadBoundedContentAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is { } length && length > maximumBytes)
            return null;

        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream(Math.Min(maximumBytes, 4096));
        var chunk = new byte[4096];
        while (true)
        {
            var read = await source.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0) return buffer.ToArray();
            if (buffer.Length + read > maximumBytes) return null;
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        if (retry?.Delta is { } delta) return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (retry?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }
        return null;
    }

    private static FoundryArtifactIngressResult InvalidReceipt(HttpStatusCode status, string code) =>
        new(FoundryArtifactIngressOutcome.Rejected, status, ErrorCode: code);
}
