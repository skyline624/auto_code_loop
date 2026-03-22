namespace AutoLoop.Core.Models;

/// <summary>
/// Résultat d'une exécution de Claude Code CLI.
/// </summary>
public sealed record ClaudeCodeResult
{
    /// <summary>Sortie standard de Claude Code.</summary>
    public required string Output { get; init; }

    /// <summary>Prompt brut envoyé à Claude Code.</summary>
    public required string RawPrompt { get; init; }

    /// <summary>Date/heure de début d'exécution.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Date/heure de fin d'exécution.</summary>
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>Code de sortie du processus.</summary>
    public required int ExitCode { get; init; }

    /// <summary>Sortie d'erreur (si exit code != 0).</summary>
    public string? ErrorOutput { get; init; }

    /// <summary>Durée de l'exécution.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>Nombre de tokens en entrée (si disponible).</summary>
    public int InputTokens { get; init; }

    /// <summary>Nombre de tokens en sortie (si disponible).</summary>
    public int OutputTokens { get; init; }

    /// <summary>Indique si l'exécution a réussi.</summary>
    public bool Success => ExitCode == 0;

    /// <summary>Phase associée à cette exécution.</summary>
    public CyclePhase Phase { get; init; }
}

/// <summary>
/// Réponse parsée de Claude Code pour la génération d'hypothèses.
/// </summary>
public sealed record ClaudeHypothesisResponse
{
    public IReadOnlyList<GeneratedHypothesis> Hypotheses { get; init; } = [];
}

public sealed record GeneratedHypothesis
{
    public required string Id { get; init; }
    public required string Description { get; init; }
    public IReadOnlyList<string> TargetFiles { get; init; } = [];
    public int ExpectedImpact { get; init; }
    public double Confidence { get; init; }
    public string Risk { get; init; } = "medium";
    public string Rationale { get; init; } = string.Empty;
}

/// <summary>
/// Réponse parsée de Claude Code pour l'application de mutations.
/// </summary>
public sealed record ClaudeMutationResponse
{
    public IReadOnlyList<FileChange> Changes { get; init; } = [];
    public string Summary { get; init; } = string.Empty;
}

public sealed record FileChange
{
    public required string FilePath { get; init; }
    public required ChangeType ChangeType { get; init; }
    public string? Diff { get; init; }
    public string? NewContent { get; init; }
}

public enum ChangeType
{
    Modify,
    Create,
    Delete
}

/// <summary>
/// Réponse parsée de Claude Code pour l'évaluation.
/// </summary>
public sealed record ClaudeEvaluationResponse
{
    public required DecisionOutcome Decision { get; init; }
    public double Confidence { get; init; }
    public double ImprovementScore { get; init; }
    public string Rationale { get; init; } = string.Empty;
    public IReadOnlyList<string> Recommendations { get; init; } = [];
}