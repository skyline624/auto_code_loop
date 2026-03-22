using AutoLoop.Core.Exceptions;
using AutoLoop.Versioning.Options;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoLoop.Versioning;

/// <summary>
/// Opérations git locales via LibGit2Sharp.
/// Gère branches, commits, push, checkout et revert.
/// </summary>
public interface ILocalGitOperations
{
    void CreateAndCheckoutBranch(string branchName);
    string CommitAll(string message);
    void Push(string branchName);
    void Pull(string branchName);
    void DeleteBranch(string branchName);
    void RevertCommit(string sha);
    void CheckoutFilesFromHead(string filePath);
    void CheckoutBranch(string branchName);
    string GetCurrentBranchName();
    string GetUnifiedDiff();
}

public sealed class LibGit2SharpOperations : ILocalGitOperations
{
    private readonly LocalGitOptions _options;
    private readonly ILogger<LibGit2SharpOperations> _logger;

    public LibGit2SharpOperations(
        IOptions<LocalGitOptions> options,
        ILogger<LibGit2SharpOperations> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public void CreateAndCheckoutBranch(string branchName)
    {
        try
        {
            using var repo = new Repository(_options.RepositoryPath);
            var branch = repo.CreateBranch(branchName);
            Commands.Checkout(repo, branch);
            _logger.LogDebug("Branche locale créée et checkoutée : {Branch}", branchName);
        }
        catch (Exception ex)
        {
            throw new VersioningException($"Impossible de créer la branche '{branchName}'.", ex);
        }
    }

    public string CommitAll(string message)
    {
        try
        {
            using var repo = new Repository(_options.RepositoryPath);
            Commands.Stage(repo, "*");

            var sig = new Signature(_options.UserName, _options.UserEmail, DateTimeOffset.UtcNow);
            var commit = repo.Commit(message, sig, sig,
                new CommitOptions { AllowEmptyCommit = false });

            _logger.LogDebug("Commit créé : {Sha}", commit.Sha[..8]);
            return commit.Sha;
        }
        catch (Exception ex)
        {
            throw new VersioningException($"Impossible de créer le commit : {ex.Message}", ex);
        }
    }

    public void Push(string branchName)
    {
        try
        {
            using var repo = new Repository(_options.RepositoryPath);
            var remote = repo.Network.Remotes["origin"];

            var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                ?? throw new InvalidOperationException("GITHUB_TOKEN non défini.");

            var pushOptions = new PushOptions
            {
                CredentialsProvider = (_, _, _) =>
                    new UsernamePasswordCredentials
                    {
                        Username = token,
                        Password = string.Empty
                    }
            };

            repo.Network.Push(remote, $"refs/heads/{branchName}", pushOptions);
            _logger.LogDebug("Push effectué pour la branche : {Branch}", branchName);
        }
        catch (Exception ex)
        {
            throw new VersioningException($"Impossible de pusher la branche '{branchName}'.", ex);
        }
    }

    public void Pull(string branchName)
    {
        try
        {
            using var repo = new Repository(_options.RepositoryPath);
            var sig = new Signature(_options.UserName, _options.UserEmail, DateTimeOffset.UtcNow);

            Commands.Checkout(repo, branchName);
            Commands.Pull(repo, sig, new PullOptions());
            _logger.LogDebug("Pull effectué sur la branche : {Branch}", branchName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erreur lors du pull de la branche {Branch}.", branchName);
        }
    }

    public void DeleteBranch(string branchName)
    {
        try
        {
            using var repo = new Repository(_options.RepositoryPath);
            var branch = repo.Branches[branchName];
            if (branch != null)
            {
                repo.Branches.Remove(branch);
                _logger.LogDebug("Branche locale supprimée : {Branch}", branchName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de supprimer la branche locale {Branch}.", branchName);
        }
    }

    public void RevertCommit(string sha)
    {
        try
        {
            using var repo = new Repository(_options.RepositoryPath);
            var commit = repo.Lookup<Commit>(sha)
                ?? throw new ArgumentException($"Commit {sha} introuvable.");

            var sig = new Signature(_options.UserName, _options.UserEmail, DateTimeOffset.UtcNow);
            repo.Revert(commit, sig, new RevertOptions { CommitOnSuccess = true });
            _logger.LogDebug("Revert du commit {Sha} effectué.", sha[..8]);
        }
        catch (Exception ex)
        {
            throw new VersioningException($"Impossible de reverter le commit {sha}.", ex);
        }
    }

    public void CheckoutFilesFromHead(string filePath)
    {
        try
        {
            using var repo = new Repository(_options.RepositoryPath);
            repo.CheckoutPaths("HEAD",
                new[] { filePath },
                new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force });

            _logger.LogDebug("Fichier restauré depuis HEAD : {File}", filePath);
        }
        catch (Exception ex)
        {
            throw new VersioningException($"Impossible de restaurer le fichier '{filePath}'.", ex);
        }
    }

    public void CheckoutBranch(string branchName)
    {
        try
        {
            using var repo = new Repository(_options.RepositoryPath);
            Commands.Checkout(repo, branchName);
        }
        catch (Exception ex)
        {
            throw new VersioningException($"Impossible de checkout la branche '{branchName}'.", ex);
        }
    }

    public string GetCurrentBranchName()
    {
        using var repo = new Repository(_options.RepositoryPath);
        return repo.Head.FriendlyName;
    }

    public string GetUnifiedDiff()
    {
        using var repo = new Repository(_options.RepositoryPath);
        var headTree = repo.Head.Tip?.Tree;
        var patch = headTree != null
            ? repo.Diff.Compare<Patch>(headTree, DiffTargets.WorkingDirectory | DiffTargets.Index)
            : repo.Diff.Compare<Patch>(null, DiffTargets.WorkingDirectory | DiffTargets.Index);
        return patch.Content;
    }
}
