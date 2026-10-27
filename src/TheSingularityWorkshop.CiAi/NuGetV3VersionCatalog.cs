using System.Net;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Reads the published version index for one package from a NuGet V3 flat-container feed.
/// This is a read-only observation; it does not classify compatibility or modify dependencies.
/// </summary>
public interface INuGetVersionCatalog
{
    Task<NuGetVersionCatalogObservation> ObserveAsync(
        string packageId,
        CancellationToken cancellationToken = default);
}

/// <summary>A point-in-time, attributable observation of a package's published version strings.</summary>
public sealed record NuGetVersionCatalogObservation(
    string PackageId,
    Uri Feed,
    Uri Resource,
    DateTimeOffset ObservedAtUtc,
    IReadOnlyList<string> Versions);

/// <summary>
/// Read-only NuGet V3 flat-container version discovery. The supplied HttpClient remains owned
/// by the caller so applications can configure timeouts, handlers, and lifetime.
/// </summary>
public sealed class NuGetV3VersionCatalog : INuGetVersionCatalog
{
    private readonly HttpClient _httpClient;
    private readonly Uri _flatContainerBaseUri;
    private readonly TimeProvider _timeProvider;

    public NuGetV3VersionCatalog(
        HttpClient httpClient,
        Uri? flatContainerBaseUri = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _flatContainerBaseUri = flatContainerBaseUri ??
            new Uri("https://api.nuget.org/v3-flatcontainer/", UriKind.Absolute);
        if (!_flatContainerBaseUri.IsAbsoluteUri ||
            _flatContainerBaseUri.Scheme != Uri.UriSchemeHttps ||
            !_flatContainerBaseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("The flat-container base URI must be an absolute HTTPS URI ending in '/'.",
                nameof(flatContainerBaseUri));

        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<NuGetVersionCatalogObservation> ObserveAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        if (!string.Equals(packageId, packageId.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Package ID cannot have leading or trailing whitespace.", nameof(packageId));

        var normalizedId = packageId.ToLowerInvariant();
        var resource = new Uri(_flatContainerBaseUri, Uri.EscapeDataString(normalizedId) + "/index.json");
        using var request = new HttpRequestMessage(HttpMethod.Get, resource);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("versions", out var versionsElement) ||
            versionsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("NuGet version index must contain a 'versions' array.");

        var versions = new List<string>();
        foreach (var element in versionsElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(element.GetString()))
                throw new InvalidDataException("NuGet version index contains a missing or non-string version.");
            var version = element.GetString()!;
            if (!string.Equals(version, version.Trim(), StringComparison.Ordinal))
                throw new InvalidDataException("NuGet version index contains a version with surrounding whitespace.");
            versions.Add(version);
        }

        if (versions.Count == 0)
            throw new InvalidDataException("NuGet version index contains no published versions.");

        return new NuGetVersionCatalogObservation(
            packageId,
            _flatContainerBaseUri,
            resource,
            _timeProvider.GetUtcNow(),
            versions.AsReadOnly());
    }
}
