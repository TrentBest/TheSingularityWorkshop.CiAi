using System.Text.RegularExpressions;
using TheSingularityWorkshop.CommandAi;

namespace TheSingularityWorkshop.CiAi.RepositoryCommands;

/// <summary>Stable CiAi action and parameter identities carried by CommandAi/1 proposals.</summary>
/// <remarks>These identities describe repository-development intent only. They are not host capabilities or authorization.</remarks>
public static class AiCiCommandCatalog
{
    public const ulong CreateProject = 1001;
    public const ulong AddProjectFile = 1002;
    public const ulong ModifyProjectFile = 1003;
    public const ulong AddDependency = 1004;
    public const ulong RequestBuild = 1005;
    public const ulong RequestTests = 1006;

    public const ulong Repository = 1;
    public const ulong Branch = 2;
    public const ulong BaseSha = 3;
    public const ulong NameOrPath = 4;
    public const ulong ContentOrExpectedSha = 5;
    public const ulong PatchOrVersion = 6;
}

/// <summary>
/// Pure fluent composer for bounded repository-development proposals.
/// It creates CommandAi data; it does not access Git, execute tools, or authorize changes.
/// </summary>
public sealed class AiCiRepositoryBuilder
{
    private static readonly Regex RepositoryPattern = new("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant);
    private static readonly Regex CommitShaPattern = new("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant);
    private static readonly Regex SourceSha256Pattern = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);
    private readonly string _repository;
    private readonly string _branch;
    private readonly string _baseSha;
    private readonly List<CommandInstruction> _commands = [];

    internal AiCiRepositoryBuilder(string repository, string branch, string baseSha)
    {
        if (string.IsNullOrWhiteSpace(repository) || !RepositoryPattern.IsMatch(repository) ||
            repository.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Repository must be an owner/repository identity.", nameof(repository));
        if (string.IsNullOrWhiteSpace(branch) ||
            !Regex.IsMatch(branch, "^[A-Za-z0-9][A-Za-z0-9._/-]{0,119}$", RegexOptions.CultureInvariant) ||
            branch.Equals("master", StringComparison.OrdinalIgnoreCase) ||
            branch.Equals("development", StringComparison.OrdinalIgnoreCase) ||
            branch.StartsWith("refs/", StringComparison.OrdinalIgnoreCase) ||
            branch.Split('/').Any(part => part is "" or "." or ".."))
            throw new ArgumentException("Target must be an isolated feature branch, never master or development.", nameof(branch));
        if (string.IsNullOrWhiteSpace(baseSha) || !CommitShaPattern.IsMatch(baseSha))
            throw new ArgumentException("Base commit must be a full 40-character hexadecimal SHA.", nameof(baseSha));

        _repository = repository;
        _branch = branch;
        _baseSha = baseSha.ToLowerInvariant();
    }

    /// <summary>Adds a project-creation intent.</summary>
    public AiCiRepositoryBuilder CreateProject(string projectName)
    {
        ValidateText(projectName, nameof(projectName));
        Add(AiCiCommandCatalog.CreateProject,
            (AiCiCommandCatalog.NameOrPath, projectName));
        return this;
    }

    /// <summary>Adds a new bounded project file.</summary>
    public AiCiRepositoryBuilder AddProjectFile(string path, string content)
    {
        ValidatePath(path);
        ValidatePayload(content, nameof(content));
        Add(AiCiCommandCatalog.AddProjectFile,
            (AiCiCommandCatalog.NameOrPath, path),
            (AiCiCommandCatalog.ContentOrExpectedSha, content));
        return this;
    }

    /// <summary>Adds a new script file; an alias for AddProjectFile.</summary>
    public AiCiRepositoryBuilder AddScript(string path, string source) => AddProjectFile(path, source);

    /// <summary>Proposes replacing a file only when its current content matches the expected SHA-256.</summary>
    public AiCiRepositoryBuilder ModifyProjectFile(string path, string expectedSourceSha256, string patch)
    {
        ValidatePath(path);
        if (string.IsNullOrWhiteSpace(expectedSourceSha256) || !SourceSha256Pattern.IsMatch(expectedSourceSha256))
            throw new ArgumentException("Modify operations require the expected full 64-character SHA-256 digest of the current file content.", nameof(expectedSourceSha256));
        ValidatePayload(patch, nameof(patch));
        Add(AiCiCommandCatalog.ModifyProjectFile,
            (AiCiCommandCatalog.NameOrPath, path),
            (AiCiCommandCatalog.ContentOrExpectedSha, expectedSourceSha256.ToLowerInvariant()),
            (AiCiCommandCatalog.PatchOrVersion, patch));
        return this;
    }

    /// <summary>Compatibility name for ModifyProjectFile.</summary>
    public AiCiRepositoryBuilder ModifyScript(string path, string expectedSourceSha, string patch)
        => ModifyProjectFile(path, expectedSourceSha, patch);

    /// <summary>Adds a package dependency intent; this does not restore or install it.</summary>
    public AiCiRepositoryBuilder AddDependency(string packageId, string version)
    {
        ValidateText(packageId, nameof(packageId));
        ValidateText(version, nameof(version));
        Add(AiCiCommandCatalog.AddDependency,
            (AiCiCommandCatalog.NameOrPath, packageId),
            (AiCiCommandCatalog.ContentOrExpectedSha, version));
        return this;
    }

    /// <summary>Requests a build from the authorized host.</summary>
    public AiCiRepositoryBuilder RequestBuild()
    {
        Add(AiCiCommandCatalog.RequestBuild);
        return this;
    }

    /// <summary>Requests tests from the authorized host.</summary>
    public AiCiRepositoryBuilder RequestTests()
    {
        Add(AiCiCommandCatalog.RequestTests);
        return this;
    }

    /// <summary>Returns an immutable CommandAi/1 proposal snapshot.</summary>
    public CommandProposal Build()
    {
        if (_commands.Count == 0)
            throw new InvalidOperationException("A repository proposal must contain at least one command.");
        return new CommandProposal(_commands.ToArray());
    }

    private void Add(ulong actionId, params (ulong Id, string Value)[] values)
    {
        if (_commands.Count >= CommandLimits.MaxCommands)
            throw new InvalidOperationException($"A proposal cannot contain more than {CommandLimits.MaxCommands} commands.");
        var args = new List<CommandArgument>
        {
            new(AiCiCommandCatalog.Repository, CommandValue.FromText(_repository)),
            new(AiCiCommandCatalog.Branch, CommandValue.FromText(_branch)),
            new(AiCiCommandCatalog.BaseSha, CommandValue.FromText(_baseSha))
        };
        args.AddRange(values.Select(item => new CommandArgument(item.Id, CommandValue.FromText(item.Value))));
        _commands.Add(new CommandInstruction(actionId, args));
    }

    private static void ValidatePath(string path)
    {
        ValidateText(path, nameof(path));
        if (path.Length > 240 || path.StartsWith("/", StringComparison.Ordinal) ||
            path.StartsWith("\\", StringComparison.Ordinal) || path.Contains('\\') ||
            path.Contains(':') || path.Split('/').Any(part => part is "" or "." or "..") ||
            path.Any(char.IsControl))
            throw new ArgumentException("Path must be a bounded, normalized repository-relative path without traversal or platform-specific roots.", nameof(path));
    }

    private static void ValidatePayload(string value, string parameterName)
    {
        ValidateText(value, parameterName);
        if (value.Length > CommandLimits.MaxLiteralCharacters)
            throw new ArgumentException($"Payload cannot exceed {CommandLimits.MaxLiteralCharacters} characters in one CommandAi literal.", parameterName);
    }

    private static void ValidateText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be empty or whitespace.", parameterName);
        if (value.Any(char.IsControl) && parameterName != "content" && parameterName != "patch")
            throw new ArgumentException("Identity and path values cannot contain control characters.", parameterName);
    }
}

/// <summary>Entry point for creating a bounded repository-development proposal.</summary>
public static class AiCi
{
    /// <summary>Starts a proposal bound to an isolated feature branch and exact base commit.</summary>
    public static AiCiRepositoryBuilder ForRepository(string repository, string featureBranch, string baseSha)
        => new(repository, featureBranch, baseSha);
}
