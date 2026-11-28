using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Transport boundary for sending a verified dependency delivery to one named recipient.
/// A queued result is not an acceptance receipt; only a receiver-issued receipt closes delivery.
/// </summary>
public interface ICiAiDependencyRegistrationTransport
{
    ValueTask<CiAiDependencyRegistrationTransportResult> DispatchAsync(
        CiAiDependencyRegistrationDelivery delivery, string targetRepository,
        CancellationToken cancellationToken = default);
}

public enum CiAiDependencyRegistrationTransportDisposition
{
    Queued,
    RetryableFailure,
    PermanentFailure
}

public sealed record CiAiDependencyRegistrationTransportResult(
    CiAiDependencyRegistrationTransportDisposition Disposition,
    int? HttpStatusCode,
    string OutcomeCode);

/// <summary>
/// Supplies a short-lived GitHub App installation token scoped to the target repository.
/// Implementations must not persist or log the token.
/// </summary>
public interface ICiAiGitHubInstallationTokenProvider
{
    ValueTask<string> GetInstallationTokenAsync(
        string targetRepository, CancellationToken cancellationToken = default);
}

/// <summary>
/// GitHub repository_dispatch adapter. HTTP 204 means the event was queued only; it is not
/// evidence that the receiver workflow ran, verified the source, or persisted the ledger.
/// </summary>
public sealed class GitHubDependencyRegistrationDispatchTransport : ICiAiDependencyRegistrationTransport
{
    public const string EventType = "ciai_dependency_registration_v1";
    private readonly HttpClient _httpClient;
    private readonly ICiAiGitHubInstallationTokenProvider _tokenProvider;

    public GitHubDependencyRegistrationDispatchTransport(HttpClient httpClient,
        ICiAiGitHubInstallationTokenProvider tokenProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    }

    public async ValueTask<CiAiDependencyRegistrationTransportResult> DispatchAsync(
        CiAiDependencyRegistrationDelivery delivery, string targetRepository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        if (!RepositoryIdentity.IsValid(targetRepository) ||
            !delivery.RecipientRepositories.Contains(targetRepository, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Dispatch target must be an explicit canonical delivery recipient.",
                nameof(targetRepository));

        // GitHub requires client_payload to be strictly smaller than 64 KiB.
        // Measure the serialized UTF-8 payload, including JSON escaping of delivery.Payload.
        var clientPayload = JsonSerializer.Serialize(new
        {
            deliveryJson = delivery.Payload,
            deliveryId = delivery.DeliveryId,
            payloadSha256 = delivery.PayloadSha256
        });
        if (Encoding.UTF8.GetByteCount(clientPayload) >= 64 * 1024)
            return new(CiAiDependencyRegistrationTransportDisposition.PermanentFailure, null,
                "github.client-payload-too-large");

        string token;
        try
        {
            token = await _tokenProvider.GetInstallationTokenAsync(targetRepository, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return new(CiAiDependencyRegistrationTransportDisposition.RetryableFailure, null,
                "github.token-provider-transport-error");
        }
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("GitHub installation token provider returned no token.");

        var segments = targetRepository.Split('/');
        var endpoint = new Uri($"https://api.github.com/repos/{Uri.EscapeDataString(segments[0])}/" +
            $"{Uri.EscapeDataString(segments[1])}/dispatches");
        var payload = JsonSerializer.Serialize(new
        {
            event_type = EventType,
            client_payload = JsonSerializer.Deserialize<JsonElement>(clientPayload)
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("TheSingularityWorkshop-CiAi/1.0");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return new(CiAiDependencyRegistrationTransportDisposition.RetryableFailure, null,
                "github.transport-error");
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (response.StatusCode == HttpStatusCode.NoContent)
                return new(CiAiDependencyRegistrationTransportDisposition.Queued, status, "github.event-queued");
            if (response.StatusCode == HttpStatusCode.TooManyRequests || status >= 500)
                return new(CiAiDependencyRegistrationTransportDisposition.RetryableFailure, status,
                    "github.transient-response");
            return new(CiAiDependencyRegistrationTransportDisposition.PermanentFailure, status,
                "github.dispatch-rejected");
        }
    }
}
