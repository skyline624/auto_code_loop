using System.Diagnostics;
using AutoLoop.Core.Exceptions;
using AutoLoop.Core.Interfaces;
using AutoLoop.Core.Models;
using AutoLoop.Versioning;
using Microsoft.Extensions.Logging;

namespace AutoLoop.Rollback;

/// <summary>
/// Gestionnaire de rollback à 3 niveaux.
/// Tier 1 : restauration in-memory (OriginalContent du ChangeRecord).
/// Tier 2 : git checkout du fichier depuis HEAD.
/// Tier 3 : git revert du commit (conserve l'historique).
/// </summary>
public sealed class RollbackManager : IRollbackManager
{
    private readonly IRollbackPolicy _policy;
    private readonly ILocalGitOperations _localGit;
    private readonly IHealthChecker _healthChecker;
    private readonly ILogger<RollbackManager> _logger;

    public RollbackManager(
        IRollbackPolicy policy,
        ILocalGitOperations localGit,
        IHealthChecker healthChecker,
        ILogger<RollbackManager> logger)
    {
        _policy = policy;
        _localGit = localGit;
        _healthChecker = healthChecker;
        _logger = logger;
    }

    public async Task<RollbackResult> RollbackAsync(
        CycleContext context,
        RollbackReason reason,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var strategy = _policy.SelectStrategy(context, reason);

        _logger.LogWarning(
            "Rollback démarré. Cycle={CycleId}, Raison={Reason}, Stratégie={Strategy}",
            context.CycleId, reason, strategy);

        try
        {
            await ExecuteStrategyAsync(context, strategy, ct);

            // Vérification post-rollback
            var health = await _healthChecker.CheckAsync(context, ct);

            if (!health.IsHealthy)
            {
                _logger.LogError(
                    "Health check post-rollback ÉCHOUÉ : {Failures}",
                    string.Join(", ", health.FailedChecks));

                // Tenter la stratégie suivante si possible
                var fallbackStrategy = GetFallbackStrategy(strategy, context);
                if (fallbackStrategy.HasValue)
                {
                    _logger.LogWarning("Tentative de fallback avec stratégie : {FallbackStrategy}", fallbackStrategy.Value);
                    await ExecuteStrategyAsync(context, fallbackStrategy.Value, ct);
                    health = await _healthChecker.CheckAsync(context, ct);
                }

                if (!health.IsHealthy)
                {
                    throw new CriticalRollbackFailureException(
                        context.CycleId,
                        $"Toutes les stratégies de rollback ont échoué. Issues : {string.Join(", ", health.FailedChecks)}");
                }
            }

            _logger.LogInformation(
                "Rollback réussi. Stratégie={Strategy}, Durée={Ms}ms",
                strategy, (int)sw.ElapsedMilliseconds);

            return new RollbackResult
            {
                Succeeded = true,
                Reason = reason,
                StrategyUsed = strategy,
                ExecutedAt = DateTimeOffset.UtcNow,
                Duration = sw.Elapsed
            };
        }
        catch (CriticalRollbackFailureException)
        {
            throw; // Remonter pour arrêt du système
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex,
                "Rollback CRITIQUE échoué pour le cycle {CycleId}.",
                context.CycleId);

            return new RollbackResult
            {
                Succeeded = false,
                Reason = reason,
                StrategyUsed = strategy,
                ExecutedAt = DateTimeOffset.UtcNow,
                Duration = sw.Elapsed,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task ExecuteStrategyAsync(
        CycleContext context, RollbackStrategy strategy, CancellationToken ct)
    {
        switch (strategy)
        {
            case RollbackStrategy.InMemoryRestore when context.AppliedChange is not null:
                _logger.LogDebug("Rollback Tier 1 : restauration in-memory pour {File}.",
                    context.AppliedChange.FilePath);
                await File.WriteAllTextAsync(
                    context.AppliedChange.FilePath,
                    context.AppliedChange.OriginalContent,
                    ct);
                break;

            case RollbackStrategy.GitCheckout when context.AppliedChange is not null:
                _logger.LogDebug("Rollback Tier 2 : git checkout HEAD pour {File}.",
                    context.AppliedChange.FilePath);
                await Task.Run(
                    () => _localGit.CheckoutFilesFromHead(context.AppliedChange.FilePath),
                    ct);
                break;

            case RollbackStrategy.GitRevert when context.AppliedChange?.CommitSha is not null:
                _logger.LogDebug("Rollback Tier 3 : git revert {Sha}.",
                    context.AppliedChange.CommitSha[..8]);
                await Task.Run(
                    () => _localGit.RevertCommit(context.AppliedChange.CommitSha!),
                    ct);
                break;

            default:
                _logger.LogWarning(
                    "Aucune action de rollback possible (stratégie={Strategy}, change={HasChange}).",
                    strategy, context.AppliedChange != null);
                break;
        }
    }

    private static RollbackStrategy? GetFallbackStrategy(
        RollbackStrategy current, CycleContext context)
    {
        return current switch
        {
            RollbackStrategy.InMemoryRestore =>
                context.AppliedChange?.CommitSha != null
                    ? RollbackStrategy.GitRevert
                    : RollbackStrategy.GitCheckout,

            RollbackStrategy.GitCheckout =>
                context.AppliedChange?.CommitSha != null
                    ? RollbackStrategy.GitRevert
                    : null,

            RollbackStrategy.GitRevert => null,
            _ => null
        };
    }
}
