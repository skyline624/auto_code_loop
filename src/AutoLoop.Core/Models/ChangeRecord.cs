namespace AutoLoop.Core.Models;

/// <summary>
/// Unité centrale de traçabilité : chaque modification de code porte un UUID immuable.
/// Contient aussi OriginalContent pour le rollback in-memory (Tier 1).
/// </summary>
public sealed record ChangeRecord
{
    public required Guid Id { get; init; }
    public required CycleId CycleId { get; init; }
    public required string HypothesisId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string FilePath { get; init; }
    public required string OriginalContent { get; init; }
    public required string MutatedContent { get; init; }
    public required string UnifiedDiff { get; init; }
    public required MutationType MutationType { get; init; }
    public required string Rationale { get; init; }

    /// <summary>SHA du commit Git associé (null si pas encore commité).</summary>
    public string? CommitSha { get; init; }

    /// <summary>URL de la Pull Request GitHub (null si pas encore créée).</summary>
    public string? PullRequestUrl { get; init; }

    // === Champs Claude Code (NOUVEAU) ===

    /// <summary>Prompt envoyé à Claude Code pour générer cette mutation.</summary>
    public string? ClaudeCodePrompt { get; init; }

    /// <summary>Réponse brute de Claude Code.</summary>
    public string? ClaudeCodeResponse { get; init; }

    /// <summary>Fichiers additionnels modifiés par la même mutation.</summary>
    public IReadOnlyList<string> AdditionalFiles { get; init; } = [];
}
