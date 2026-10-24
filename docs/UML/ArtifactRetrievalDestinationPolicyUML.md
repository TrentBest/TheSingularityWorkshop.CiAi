# ArtifactRetrievalDestinationPolicy — UML and Use Case

```mermaid
classDiagram
    class ArtifactManifest
    class ArtifactRetrievalDestinationPolicy {
        - HashSet~string~ allowedHosts
        + Validate(Uri destination, DateTimeOffset now, DateTimeOffset expiresAt) IReadOnlyList~string~
    }
    ArtifactRetrievalDestinationPolicy ..> ArtifactManifest : policy applies to RetrievalUri and ExpiresAt
```

## Use case: validate a destination before opening a connection

```mermaid
flowchart TD
    A[Receive artifact manifest] --> B[Read configured provider host allowlist]
    B --> C[Validate HTTPS, host, port, credentials, fragment, expiry]
    C --> D{Any policy errors?}
    D -- Yes --> E[Reject without connecting]
    D -- No --> F[Transport performs DNS/IP and redirect safety checks]
    F --> G[Authenticated bounded retrieval]
    G --> H[Verify byte count and SHA-256]
```

The class is a preflight policy gate only. It does not resolve DNS, perform network requests, authenticate, or make redirects safe. The caller must enforce those controls at the transport boundary.
