namespace TheSingularityWorkshop.CiAi;

public static partial class CiAiReadmeBadgeStamper
{
    /// <summary>
    /// Returns repository-relative README.md paths whose managed badge block differs from the
    /// supplied configuration. This method is read-only and uses the same exclusion policy as stamping.
    /// </summary>
    public static IReadOnlyList<string> FindUnstampedRepositoryReadmes(
        string repositoryRoot,
        CiAiRepositoryConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(configuration);
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var stale = new List<string>();
        foreach (var path in EnumerateReadmes(root))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                continue;
            var original = File.ReadAllText(path);
            if (!string.Equals(original, Stamp(original, configuration), StringComparison.Ordinal))
                stale.Add(Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'));
        }
        return Array.AsReadOnly(stale.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    private static IEnumerable<string> EnumerateReadmes(string root)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".git", "bin", "obj", "node_modules", ".vs" };
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly))
                if (string.Equals(Path.GetFileName(file), "README.md", StringComparison.OrdinalIgnoreCase))
                    yield return file;
            foreach (var directory in Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(directory);
                if (excluded.Contains(name) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    continue;
                pending.Push(directory);
            }
        }
    }
}
