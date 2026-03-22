namespace AutoLoop.Versioning.Options;

public sealed class GitHubOptions
{
    public const string Section = "GitHub";
    public required string Owner { get; set; }
    public required string Repository { get; set; }
    public string DefaultBranch { get; set; } = "main";
}

public sealed class LocalGitOptions
{
    public const string Section = "LocalGit";
    public string RepositoryPath { get; set; } = ".";
    public string UserName { get; set; } = "AutoLoop Bot";
    public string UserEmail { get; set; } = "autoloop@system.local";
}
