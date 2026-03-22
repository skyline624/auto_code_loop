using System.Diagnostics;
using System.Text;
using AutoLoop.Core.Exceptions;
using AutoLoop.Core.Interfaces;
using AutoLoop.Core.Models;
using AutoLoop.Versioning.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace AutoLoop.Versioning;

/// <summary>
/// Backend de versioning GitHub via Octokit.NET.
/// Gère : branches, commits (local + push), Pull Requests, merge.
/// Résilience via Polly : 3 retries exponentiels + circuit breaker.
/// </summary>
public sealed class GitHubBackend : IVersioningBackend
{
    private readonly GitHubClient _client;
    private readonly ILocalGitOperations _localGit;
    private readonly IMetricsRegistry _metrics;
    private readonly GitHubOptions _githubOptions;
    private readonly ILogger<GitHubBackend> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;

    public GitHubBackend(
        IOptions<GitHubOptions> githubOptions,
        IOptions<LocalGitOptions> localGitOptions,
        ILocalGitOperations localGit,
        IMetricsRegistry metrics,
        ILogger<GitHubBackend> logger)
    {
        _githubOptions = githubOptions.Value;
        _localGit = localGit;
        _metrics = metrics;
        _logger = logger;

        if (_githubOptions.Owner == "CHANGE_ME" || _githubOptions.Repository == "CHANGE_ME")
            throw new InvalidOperationException(
                "GitHub non configuré : définissez 'GitHub:Owner' et 'GitHub:Repository' dans appsettings.json.");

        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
            ?? throw new InvalidOperationException(
                "La variable d'environnement GITHUB_TOKEN n'est pas définie.");

        _client = new GitHubClient(new ProductHeaderValue("AutoLoop-Bot"))
        {
            Credentials = new Credentials(token)
        };

        _resiliencePipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "Retry #{Attempt} pour l'opération GitHub après {Delay}.",
                        args.AttemptNumber + 1, args.RetryDelay);
                    return ValueTask.CompletedTask;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromMinutes(1),
                OnOpened = args =>
                {
                    _logger.LogError("Circuit breaker GitHub OUVERT. Pause de 1 minute.");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public async Task<string> CreateBranchAsync(string branchName, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        bool success = false;

        try
        {
            // 1) Création locale
            _localGit.CreateAndCheckoutBranch(branchName);

            // 2) Création distante via Octokit
            await _resiliencePipeline.ExecuteAsync(async token =>
            {
                var mainRef = await _client.Git.Reference.Get(
                    _githubOptions.Owner,
                    _githubOptions.Repository,
                    $"heads/{_githubOptions.DefaultBranch}");

                await _client.Git.Reference.Create(
                    _githubOptions.Owner,
                    _githubOptions.Repository,
                    new NewReference($"refs/heads/{branchName}", mainRef.Object.Sha));
            }, ct);

            success = true;
            _logger.LogInformation("Branche GitHub créée : {Branch}", branchName);
            return branchName;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new GitHubApiException("CreateBranch", ex);
        }
        finally
        {
            _metrics.RecordGitHubApiCall("CreateBranch", success, sw.Elapsed);
        }
    }

    public async Task<string> CommitChangesAsync(
        ChangeRecord change, string branchName, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        bool success = false;

        try
        {
            var commitMessage = BuildCommitMessage(change);

            // Commit local + push
            var sha = _localGit.CommitAll(commitMessage);
            await Task.Run(() => _localGit.Push(branchName), ct);

            success = true;
            return sha;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new GitHubApiException("CommitChanges", ex);
        }
        finally
        {
            _metrics.RecordGitHubApiCall("CommitChanges", success, sw.Elapsed);
        }
    }

    public async Task<string> CreatePullRequestAsync(CycleContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        bool success = false;

        try
        {
            var change = context.AppliedChange!;
            var eval = context.EvaluationResult!;
            var body = BuildPullRequestBody(context, eval, change);

            var pr = await _resiliencePipeline.ExecuteAsync(async token =>
                await _client.PullRequest.Create(
                    _githubOptions.Owner,
                    _githubOptions.Repository,
                    new NewPullRequest(
                        title: $"auto-loop: {change.MutationType} [{context.CycleId}]",
                        head: context.CycleId.BranchName,
                        baseRef: _githubOptions.DefaultBranch)
                    {
                        Body = body,
                        Draft = false
                    }),
                ct);

            success = true;
            _logger.LogInformation("Pull Request créée #{Number} : {Url}", pr.Number, pr.HtmlUrl);
            return pr.HtmlUrl;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new GitHubApiException("CreatePullRequest", ex);
        }
        finally
        {
            _metrics.RecordGitHubApiCall("CreatePullRequest", success, sw.Elapsed);
        }
    }

    public async Task MergePullRequestAsync(string prUrl, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        bool success = false;

        try
        {
            var prNumber = ExtractPrNumber(prUrl);

            await _resiliencePipeline.ExecuteAsync(async token =>
                await _client.PullRequest.Merge(
                    _githubOptions.Owner,
                    _githubOptions.Repository,
                    prNumber,
                    new MergePullRequest
                    {
                        MergeMethod = PullRequestMergeMethod.Squash,
                        CommitTitle = $"auto-loop merge: {prUrl}"
                    }),
                ct);

            // Sync local avec main
            await Task.Run(() => _localGit.Pull(_githubOptions.DefaultBranch), ct);

            success = true;
            _logger.LogInformation("Pull Request #{Number} mergée.", prNumber);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new GitHubApiException("MergePullRequest", ex);
        }
        finally
        {
            _metrics.RecordGitHubApiCall("MergePullRequest", success, sw.Elapsed);
        }
    }

    public async Task DeleteBranchAsync(string branchName, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        bool success = false;

        try
        {
            // Suppression distante
            await _resiliencePipeline.ExecuteAsync(async token =>
            {
                try
                {
                    await _client.Git.Reference.Delete(
                        _githubOptions.Owner,
                        _githubOptions.Repository,
                        $"heads/{branchName}");
                }
                catch (NotFoundException)
                {
                    // Déjà supprimée — OK
                }
            }, ct);

            // Suppression locale
            _localGit.DeleteBranch(branchName);

            success = true;
            _logger.LogDebug("Branche supprimée : {Branch}", branchName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Impossible de supprimer la branche {Branch}.", branchName);
        }
        finally
        {
            _metrics.RecordGitHubApiCall("DeleteBranch", success, sw.Elapsed);
        }
    }

    public async Task<IReadOnlyList<VersionEntry>> ListVersionHistoryAsync(CancellationToken ct = default)
    {
        var commits = await _resiliencePipeline.ExecuteAsync(async token =>
            await _client.Repository.Commit.GetAll(
                _githubOptions.Owner,
                _githubOptions.Repository,
                new CommitRequest { Sha = _githubOptions.DefaultBranch }),
            ct);

        return commits.Select(c => new VersionEntry
        {
            CommitSha = c.Sha,
            Message = c.Commit.Message,
            CommittedAt = c.Commit.Author.Date,
            Author = c.Commit.Author.Name,
            BranchName = _githubOptions.DefaultBranch
        }).ToList();
    }

    private static string BuildCommitMessage(ChangeRecord change) => $"""
        auto-loop({change.MutationType.ToString().ToLower()}): {Path.GetFileName(change.FilePath)}

        Change-Id: {change.Id}
        Hypothesis: {change.HypothesisId}
        Mutation-Type: {change.MutationType}
        Rationale: {change.Rationale}
        """;

    private static string BuildPullRequestBody(
        CycleContext ctx, EvaluationResult eval, ChangeRecord change)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Auto-Loop Improvement Report");
        sb.AppendLine();
        sb.AppendLine($"**Cycle ID**: `{ctx.CycleId}`");
        sb.AppendLine($"**Mutation Type**: `{change.MutationType}`");
        sb.AppendLine($"**Target File**: `{change.FilePath}`");
        sb.AppendLine();
        sb.AppendLine("### Rationale");
        sb.AppendLine(change.Rationale);
        sb.AppendLine();
        sb.AppendLine("### Statistical Evidence");
        sb.AppendLine("| Test | Statistic | P-Value | Significant | Effect Size |");
        sb.AppendLine("|------|-----------|---------|-------------|-------------|");

        foreach (var test in eval.StatisticalTests)
        {
            sb.AppendLine(
                $"| {test.TestName} | {test.Statistic:F4} | " +
                $"{(double.IsNaN(test.PValue) ? "N/A" : test.PValue.ToString("F4"))} | " +
                $"{test.IsSignificant} | {test.EffectSize:F4} |");
        }

        sb.AppendLine();
        sb.AppendLine("### Performance Improvement");
        sb.AppendLine($"- Actual: **{eval.OverallImprovementScore:F2}%**");
        sb.AppendLine($"- Required: **{eval.ThresholdComparison.RequiredImprovementPercent:F2}%**");
        sb.AppendLine();
        sb.AppendLine("### Test Results");
        sb.AppendLine($"- Unit Tests: {(eval.ThresholdComparison.UnitTestsPassed ? "✅ PASSED" : "❌ FAILED")}");
        sb.AppendLine($"- Regression: {(eval.ThresholdComparison.RegressionPassed ? "✅ PASSED" : "❌ FAILED")}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*Généré automatiquement par AutoLoop*");

        return sb.ToString();
    }

    public Task<string> GetUnifiedDiffAsync(CancellationToken ct = default)
        => Task.FromResult(_localGit.GetUnifiedDiff());

    private static int ExtractPrNumber(string prUrl)
    {
        var parts = prUrl.TrimEnd('/').Split('/');
        return int.Parse(parts[^1]);
    }
}
