namespace AutoLoop.Core.Models;

/// <summary>
/// Intention utilisateur préservée à travers les cycles d'amélioration.
/// Capturée initialement et enrichie par Claude Code.
/// </summary>
public sealed record UserIntent
{
    /// <summary>Intention originale saisie par l'utilisateur (ex: "reduce memory usage in API handlers").</summary>
    public required string OriginalIntent { get; init; }

    /// <summary>Intention expandue par Claude Code avec plus de contexte.</summary>
    public required string ExpandedIntent { get; init; }

    /// <summary>Zones du code ciblées par l'intention (ex: ["src/api/", "src/handlers/"]).</summary>
    public IReadOnlyList<string> TargetAreas { get; init; } = [];

    /// <summary>Contraintes à respecter (ex: ["don't break existing API", "maintain backward compatibility"]).</summary>
    public IReadOnlyList<string> Constraints { get; init; } = [];

    /// <summary>Type d'amélioration attendu.</summary>
    public ImprovementType ImprovementType { get; init; } = ImprovementType.General;

    /// <summary>Métriques cibles (ex: "memory", "latency", "throughput").</summary>
    public IReadOnlyList<string> TargetMetrics { get; init; } = [];

    /// <summary>Seuil d'amélioration attendu (ex: "5% memory reduction").</summary>
    public string? ExpectedThreshold { get; init; }

    /// <summary>Date de création de l'intention.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Identifiant du cycle associé.</summary>
    public required CycleId CycleId { get; init; }
}

/// <summary>
/// Types d'améliorations possibles.
/// </summary>
public enum ImprovementType
{
    General,
    Performance,
    Memory,
    Security,
    CodeQuality,
    TestCoverage,
    Refactoring,
    BugFix,
    Documentation
}