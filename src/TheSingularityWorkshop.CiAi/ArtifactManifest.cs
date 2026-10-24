namespace TheSingularityWorkshop.CiAi;

/// <summary>Immutable identity and integrity metadata for a provider-published artifact.</summary>
public sealed record ArtifactManifest
{
    public ArtifactManifest(
        string requestId,
        string operationId,
        string idempotencyKey,
        string providerRepository,
        string capabilityId,
        string capabilityVersion,
        string implementationRevision,
        string artifactId,
        string artifactVersion,
        string sourceSha,
        string digestAlgorithm,
        string digest,
        string mediaType,
        long sizeBytes,
        Uri retrievalUri,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        string retentionClass,
        string clientIdentity,
        long? providerRunId = null)
    {
        RequireText(requestId, nameof(requestId), 200);
        RequireText(operationId, nameof(operationId), 200);
        RequireText(idempotencyKey, nameof(idempotencyKey), 200);
        if (!RepositoryIdentity.IsValid(providerRepository))
            throw new ArgumentException("Provider repository must be canonical owner/repository identity.", nameof(providerRepository));
        RequireIdentifier(capabilityId, nameof(capabilityId));
        RequireText(capabilityVersion, nameof(capabilityVersion), 100);
        RequireText(implementationRevision, nameof(implementationRevision), 200);
        RequireIdentifier(artifactId, nameof(artifactId));
        RequireText(artifactVersion, nameof(artifactVersion), 100);
        if (sourceSha.Length != 40 || !sourceSha.All(Uri.IsHexDigit))
            throw new ArgumentException("Source SHA must be a full 40-character hexadecimal commit identifier.", nameof(sourceSha));
        if (!string.Equals(digestAlgorithm, "SHA-256", StringComparison.Ordinal))
            throw new ArgumentException("Only SHA-256 is supported by this contract version.", nameof(digestAlgorithm));
        if (digest.Length != 64 || !digest.All(Uri.IsHexDigit))
            throw new ArgumentException("SHA-256 digest must contain 64 hexadecimal characters.", nameof(digest));
        RequireText(mediaType, nameof(mediaType), 200);
        if (mediaType.Contains('\r') || mediaType.Contains('\n'))
            throw new ArgumentException("Media type cannot contain line breaks.", nameof(mediaType));
        if (sizeBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(sizeBytes));
        ArgumentNullException.ThrowIfNull(retrievalUri);
        if (retrievalUri.Scheme != Uri.UriSchemeHttps || !retrievalUri.IsDefaultPort ||
            !string.IsNullOrEmpty(retrievalUri.UserInfo) || !string.IsNullOrEmpty(retrievalUri.Fragment))
            throw new ArgumentException("Retrieval URI must use HTTPS without user-info or a fragment.", nameof(retrievalUri));
        if (expiresAt <= createdAt)
            throw new ArgumentException("Artifact expiry must be later than creation.", nameof(expiresAt));
        RequireIdentifier(retentionClass, nameof(retentionClass));
        RequireText(clientIdentity, nameof(clientIdentity), 200);
        if (providerRunId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(providerRunId));

        RequestId = requestId;
        OperationId = operationId;
        IdempotencyKey = idempotencyKey;
        ProviderRepository = providerRepository;
        CapabilityId = capabilityId;
        CapabilityVersion = capabilityVersion;
        ImplementationRevision = implementationRevision;
        ArtifactId = artifactId;
        ArtifactVersion = artifactVersion;
        SourceSha = sourceSha;
        DigestAlgorithm = digestAlgorithm;
        Digest = digest.ToLowerInvariant();
        MediaType = mediaType;
        SizeBytes = sizeBytes;
        RetrievalUri = retrievalUri;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        RetentionClass = retentionClass;
        ClientIdentity = clientIdentity;
        ProviderRunId = providerRunId;
    }

    public string RequestId { get; }
    public string OperationId { get; }
    public string IdempotencyKey { get; }
    public string ProviderRepository { get; }
    public string CapabilityId { get; }
    public string CapabilityVersion { get; }
    public string ImplementationRevision { get; }
    public string ArtifactId { get; }
    public string ArtifactVersion { get; }
    public string SourceSha { get; }
    public string DigestAlgorithm { get; }
    public string Digest { get; }
    public string MediaType { get; }
    public long SizeBytes { get; }
    public Uri RetrievalUri { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
    public string RetentionClass { get; }
    public string ClientIdentity { get; }
    public long? ProviderRunId { get; }

    private static void RequireIdentifier(string value, string parameterName)
    {
        RequireText(value, parameterName, 100);
        if (!value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':'))
            throw new ArgumentException("Identifier contains unsupported characters.", parameterName);
    }

    private static void RequireText(string value, string parameterName, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maxLength || value.Any(char.IsControl))
            throw new ArgumentException($"Value must be at most {maxLength} characters and contain no control characters.", parameterName);
    }
}
