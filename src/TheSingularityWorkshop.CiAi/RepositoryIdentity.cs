namespace TheSingularityWorkshop.CiAi;

/// <summary>Canonical repository identity rules shared by request validation and provider snapshots.</summary>
internal static class RepositoryIdentity
{
    public static bool IsValid(string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository) || repository.Length > 200)
            return false;

        var parts = repository.Split('/');
        return parts.Length == 2 && parts.All(IsValidSegment);
    }

    private static bool IsValidSegment(string segment) =>
        segment.Length is > 0 and <= 100 &&
        segment is not "." and not ".." &&
        segment.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}
