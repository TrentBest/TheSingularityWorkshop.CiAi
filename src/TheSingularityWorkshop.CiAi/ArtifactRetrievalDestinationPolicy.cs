using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Applies an explicit host allowlist and basic URI constraints before artifact retrieval.</summary>
/// <remarks>
/// This is a destination-policy check, not a complete SSRF defense. The HTTP transport must also disable
/// redirects or revalidate every redirect, enforce safe DNS/IP resolution (including private, loopback,
/// link-local, and IPv4-mapped IPv6 addresses), apply timeouts and byte limits, and use provider-scoped auth.
/// </remarks>
public sealed class ArtifactRetrievalDestinationPolicy
{
    private readonly HashSet<string> _allowedHosts;

    public ArtifactRetrievalDestinationPolicy(IEnumerable<string> allowedHosts)
    {
        ArgumentNullException.ThrowIfNull(allowedHosts);
        _allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in allowedHosts)
        {
            if (string.IsNullOrWhiteSpace(host) || host.Length > 253 || host.EndsWith(".", StringComparison.Ordinal) ||
                Uri.CheckHostName(host) != UriHostNameType.Dns || host.Any(char.IsControl) || host.Contains('/') || host.Contains('@'))
                throw new ArgumentException("Allowed hosts must be DNS hostnames without paths, credentials, or trailing dots.", nameof(allowedHosts));
            _allowedHosts.Add(new UriBuilder(Uri.UriSchemeHttps, host).Uri.IdnHost);
        }
        if (_allowedHosts.Count == 0)
            throw new ArgumentException("At least one retrieval host must be allowlisted.", nameof(allowedHosts));
    }

    /// <summary>Returns stable policy error codes; an empty list means the URI passed these checks.</summary>
    public IReadOnlyList<string> Validate(Uri destination, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var errors = new List<string>();
        if (!string.Equals(destination.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            errors.Add("retrieval.https-required");
        if (!string.IsNullOrEmpty(destination.UserInfo)) errors.Add("retrieval.user-info-rejected");
        if (!string.IsNullOrEmpty(destination.Query)) errors.Add("retrieval.query-rejected");
        if (!string.IsNullOrEmpty(destination.Fragment)) errors.Add("retrieval.fragment-rejected");
        if (!destination.IsDefaultPort) errors.Add("retrieval.port-not-allowed");
        if (IPAddress.TryParse(destination.Host, out _)) errors.Add("retrieval.ip-literal-rejected");
        var host = destination.IdnHost;
        if (host.EndsWith(".", StringComparison.Ordinal) || !_allowedHosts.Contains(host))
            errors.Add("retrieval.host-not-allowed");
        if (expiresAt <= now) errors.Add("retrieval.expired");
        return errors;
    }
}
