namespace AutoLoop.Core.Models;

/// <summary>
/// Résumé d'un cycle précédent pour la mémoire cross-cycles.
/// </summary>
public sealed record CycleSummary
{
    public required CycleId CycleId { get; init; }
    public required CycleStatus Status { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>Résumé de l'hypothèse principale.</summary>
    public string? HypothesisSummary { get; init; }

    /// <summary>Score d'amélioration (si accepté).</summary>
    public double? ImprovementScore { get; init; }

    /// <summary>Décision finale (Accept/Reject/Defer).</summary>
    public string? Decision { get; init; }

    /// <summary>Type de mutation appliquée.</summary>
    public string? MutationType { get; init; }

    /// <summary>Fichiers modifiés.</summary>
    public IReadOnlyList<string> ModifiedFiles { get; init; } = [];

    /// <summary>Durée du cycle.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Intention utilisateur associée.</summary>
    public string? UserIntent { get; init; }
}