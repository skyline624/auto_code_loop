namespace AutoLoop.Core.Models;

/// <summary>
/// Une hypothèse d'amélioration générée par le moteur d'analyse.
/// </summary>
public sealed record Hypothesis
{
    public required string Id { get; init; }
    public required CycleId CycleId { get; init; }
    public required HypothesisType Type { get; init; }
    public required string TargetFile { get; init; }
    public string? TargetMethod { get; init; }
    public required string Rationale { get; init; }

    /// <summary>Score de priorité calculé par HypothesisRanker (0–1).</summary>
    public required double Priority { get; init; }

    /// <summary>Impact attendu sur la métrique cible (0–1).</summary>
    public required double ExpectedImpact { get; init; }

    /// <summary>Niveau de confiance statistique (0–1).</summary>
    public required double ConfidenceScore { get; init; }

    public required IReadOnlyDictionary<string, object> Evidence { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
}

public sealed record PerformanceMetricSnapshot
{
    public required string MetricName { get; init; }
    public required double Value { get; init; }
    public required double Baseline { get; init; }
    public required double DeltaPercent { get; init; }
    public required DateTimeOffset MeasuredAt { get; init; }
}

public sealed record ErrorPattern
{
    public required string ExceptionType { get; init; }
    public required string StackTraceSignature { get; init; }
    public required int OccurrenceCount { get; init; }
    public required string SourceFile { get; init; }
    public required int SourceLine { get; init; }
}
