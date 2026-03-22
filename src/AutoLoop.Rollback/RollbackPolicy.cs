using AutoLoop.Core.Models;

namespace AutoLoop.Rollback;

/// <summary>
/// Détermine la stratégie de rollback la plus adaptée selon le contexte et la raison.
/// Priorité : InMemoryRestore > GitCheckout > GitRevert
/// </summary>
public interface IRollbackPolicy
{
    RollbackStrategy SelectStrategy(CycleContext context, RollbackReason reason);
}

public sealed class DefaultRollbackPolicy : IRollbackPolicy
{
    public RollbackStrategy SelectStrategy(CycleContext context, RollbackReason reason)
    {
        // Tier 1 : rollback in-memory (le plus rapide)
        // Disponible si la mutation a été appliquée ET le contenu original est en mémoire
        if (context.AppliedChange is not null && reason != RollbackReason.HealthCheckFailed)
            return RollbackStrategy.InMemoryRestore;

        // Tier 2 : checkout de fichier depuis HEAD (si pas encore commité)
        if (context.AppliedChange is not null && context.AppliedChange.CommitSha is null)
            return RollbackStrategy.GitCheckout;

        // Tier 3 : revert de commit (conserve l'historique)
        if (context.AppliedChange?.CommitSha is not null)
            return RollbackStrategy.GitRevert;

        // Cas où aucune mutation n'a été appliquée
        return RollbackStrategy.GitCheckout;
    }
}
