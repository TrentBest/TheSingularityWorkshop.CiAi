using System.Buffers.Binary;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Supplies an installation-scoped token for listing every repository granted to an app installation.</summary>
public interface ICiAiGitHubInstallationInventoryTokenProvider
{
    ValueTask<string> GetInstallationTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>Supplies a trusted monotonic inventory observation revision.</summary>
public interface ICiAiRepositoryInventoryRevisionProvider
{
    ValueTask<long> GetNextRevisionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the complete GitHub App installation repository inventory. Every page must be retrieved,
/// counts must reconcile, and duplicate identities or inaccessible pages fail the entire operation.
/// </summary>
public sealed class GitHubInstallationRepositoryInventoryProvider : ICiAiTrustedRepositoryInventoryProvider
{
    private const int PageSize = 100;
    private readonly HttpClient _httpClient;
    private readonly ICiAiGitHubInstallationInventoryTokenProvider _tokenProvider;
    private readonly ICiAiRepositoryInventoryRevisionProvider _revisionProvider;
    private readonly long _installationId;
    private readonly int _maximumPages;

    public GitHubInstallationRepositoryInventoryProvider(HttpClient httpClient,
        ICiAiGitHubInstallationInventoryTokenProvider tokenProvider,
        ICiAiRepositoryInventoryRevisionProvider revisionProvider,
        long installationId, int maximumPages = 1000)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _revisionProvider = revisionProvider ?? throw new ArgumentNullException(nameof(revisionProvider));
        if (installationId < 1) throw new ArgumentOutOfRangeException(nameof(installationId));
        if (maximumPages < 1) throw new ArgumentOutOfRangeException(nameof(maximumPages));
        _installationId = installationId;
        _maximumPages = maximumPages;
    }

    public async ValueTask<CiAiRepositoryInventorySnapshot> GetCompleteInventoryAsync(
        CancellationToken cancellationToken = default)
    {
        var token = await _tokenProvider.GetInstallationTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("GitHub installation inventory token provider returned no token.");

        var repositories = new List<string>();
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var evidence = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int? expectedTotal = null;
        var complete = false;

        for (var page = 1; page <= _maximumPages; page++)
        {
            var endpoint = new Uri($"https://api.github.com/installation/repositories?per_page={PageSize}&page={page}");
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.UserAgent.ParseAdd("TheSingularityWorkshop-CiAi/1.0");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var lengthPrefix = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, bytes.Length);
            evidence.AppendData(lengthPrefix);
            evidence.AppendData(bytes);

            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("total_count", out var totalElement) ||
                !totalElement.TryGetInt32(out var total) || total < 0 ||
                !root.TryGetProperty("repositories", out var repositoriesElement) ||
                repositoriesElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("GitHub installation inventory page has an invalid response shape.");

            if (expectedTotal is null) expectedTotal = total;
            else if (expectedTotal.Value != total)
                throw new InvalidDataException("GitHub installation inventory total changed during pagination.");

            var pageRepositories = repositoriesElement.EnumerateArray().ToArray();
            if (pageRepositories.Length > PageSize)
                throw new InvalidDataException("GitHub installation inventory page exceeded the requested page size.");

            foreach (var repository in pageRepositories)
            {
                if (repository.ValueKind != JsonValueKind.Object ||
                    !repository.TryGetProperty("full_name", out var nameElement) ||
                    nameElement.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("GitHub installation inventory contains a repository without full_name.");
                var name = nameElement.GetString()!;
                if (!RepositoryIdentity.IsValid(name) || !identities.Add(name))
                    throw new InvalidDataException("GitHub installation inventory contains an invalid or duplicate repository identity.");
                repositories.Add(name);
            }

            if (repositories.Count > expectedTotal.Value)
                throw new InvalidDataException("GitHub installation inventory returned more repositories than total_count.");

            if (repositories.Count == expectedTotal.Value)
            {
                complete = true;
                break;
            }

            if (pageRepositories.Length < PageSize)
                throw new InvalidDataException("GitHub installation inventory ended before total_count was satisfied.");
        }

        if (!complete)
            throw new InvalidDataException("GitHub installation inventory exceeded the configured pagination limit.");

        var revision = await _revisionProvider.GetNextRevisionAsync(cancellationToken).ConfigureAwait(false);
        var digest = Convert.ToHexString(evidence.GetHashAndReset()).ToLowerInvariant();
        return CiAiRepositoryInventorySnapshot.CreateFromTrustedProvider(
            $"github-app-installation:{_installationId}", revision, repositories,
            isComplete: true, evidencePayloadSha256: digest);
    }
}
