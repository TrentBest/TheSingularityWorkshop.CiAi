# ArtifactRetrievalDestinationPolicy

**User moniker:** Allowlisted Artifact Destination

## Purpose

Checks a manifest retrieval URI against an explicit DNS-host allowlist, HTTPS/default-port constraints, credential and fragment restrictions, IP-literal rejection, and artifact expiry before a caller attempts retrieval.

## UML class card

```text
+--------------------------------------------------+
| ArtifactRetrievalDestinationPolicy               |
+--------------------------------------------------+
| - allowedHosts : HashSet<string>                 |
+--------------------------------------------------+
| + Validate(Uri, now, expiresAt) : IReadOnlyList  |
+--------------------------------------------------+
| Empty errors = destination passed local policy   |
+--------------------------------------------------+
```

## Use case

1. Configure the exact provider/CDN hostnames allowed by the application.
2. Validate the manifest URI and expiry before opening a connection.
3. Reject unlisted hosts, insecure schemes, unusual ports, IP literals, user-info, fragments, and expired artifacts.
4. The network client must still disable or revalidate redirects, safely resolve DNS and reject private/loopback/link-local destinations, scope credentials to the provider, enforce timeouts, and bound the streamed payload.

**Security boundary:** this is a useful first policy gate, not a standalone SSRF defense and not an HTTP retrieval client.
