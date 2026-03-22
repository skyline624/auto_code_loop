namespace AutoLoop.Core.Models;

/// <summary>
/// Enveloppe de données partagée entre toutes les phases d'un cycle.
/// Chaque phase lit depuis le contexte et y écrit ses résultats.
/// </summary>
public sealed class CycleContext
{
    public CycleId CycleId { get; init; } = CycleId.New();
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    public CyclePhase CurrentPhase { get; set; } = CyclePhase.HypothesisGeneration;
    public CycleStatus Status { get; set; } = CycleStatus.Running;

    // === Contexte projet et intention (NOUVEAU) ===

    /// <summary>Informations sur le projet cible détecté.</summary>
    public ProjectInfo? Project { get; set; }

    /// <summary>Intention utilisateur capturée et préservée.</summary>
    public UserIntent? UserIntent { get; set; }

    /// <summary>Identifiant de session Claude Code pour la continuité.</summary>
    public string? ClaudeCodeSessionId { get; set; }

    // === Résultats des phases ===

    // Phase 1 — Hypothesis
    public IReadOnlyList<Hypothesis> Hypotheses { get; set; } = [];

    // Phase 2 — Mutation
    public ChangeRecord? AppliedChange { get; set; }

    // Phase 3 — Testing
    public TestSuite? TestResults { get; set; }

    // Phase 4 — Evaluation
    public EvaluationResult? EvaluationResult { get; set; }

    // === Mémoire entre cycles (NOUVEAU) ===

    /// <summary>Résumé des cycles précédents pour le contexte.</summary>
    public IReadOnlyList<CycleSummary> PreviousCycles { get; set; } = [];

    /// <summary>Résultat de l'exécution Claude Code de la phase actuelle.</summary>
    public ClaudeCodeResult? LastClaudeCodeResult { get; set; }

    // Rollback
    public RollbackResult? RollbackResult { get; set; }

    public string? ErrorMessage { get; set; }

    public Dictionary<string, object> Metadata { get; } = new();

    public TimeSpan Duration => (CompletedAt ?? DateTimeOffset.UtcNow) - StartedAt;
}
