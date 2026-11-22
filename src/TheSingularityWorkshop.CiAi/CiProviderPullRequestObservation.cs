namespace TheSingularityWorkshop.CiAi;

/// <summary>Pull-request head/base facts independently returned by the GitHub workflow-run API.</summary>
public sealed record CiProviderPullRequestObservation
{
    public CiProviderPullRequestObservation(int number, string headRef, string headSha, string baseRef, string baseSha)
    {
        if (number <= 0) throw new ArgumentOutOfRangeException(nameof(number));
        RequireRef(headRef, nameof(headRef));
        RequireRef(baseRef, nameof(baseRef));
        ValidateSha(headSha, nameof(headSha));
        ValidateSha(baseSha, nameof(baseSha));
        Number = number;
        HeadRef = headRef;
        HeadSha = headSha.ToLowerInvariant();
        BaseRef = baseRef;
        BaseSha = baseSha.ToLowerInvariant();
    }

    public int Number { get; }
    public string HeadRef { get; }
    public string HeadSha { get; }
    public string BaseRef { get; }
    public string BaseSha { get; }

    private static void RequireRef(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (value.Length > 255 || value.Any(char.IsControl) || value.Any(char.IsWhiteSpace) ||
            value.StartsWith("/", StringComparison.Ordinal) || value.EndsWith("/", StringComparison.Ordinal) ||
            value.Contains("..", StringComparison.Ordinal) || value.Contains("//", StringComparison.Ordinal))
            throw new ArgumentException("Provider pull-request ref is invalid.", parameter);
    }

    private static void ValidateSha(string value, string parameter)
    {
        if (value is not { Length: 40 } || !value.All(Uri.IsHexDigit))
            throw new ArgumentException("Provider pull-request commit must be a full SHA.", parameter);
    }
}
