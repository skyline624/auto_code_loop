namespace AutoLoop.ClaudeCode.Options;

/// <summary>
/// Options de configuration pour l'exécuteur Claude Code.
/// </summary>
public sealed class ClaudeCodeOptions
{
    public const string Section = "ClaudeCode";

    /// <summary>Chemin vers l'exécutable Claude Code CLI (défaut: "claude").</summary>
    public string Executable { get; set; } = "claude";

    /// <summary>Modèle Claude à utiliser (défaut: claude-sonnet-4-6).</summary>
    public string DefaultModel { get; set; } = "claude-sonnet-4-6";

    /// <summary>Nombre maximum de tokens en sortie.</summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>Timeout d'exécution en millisecondes.</summary>
    public int TimeoutMs { get; set; } = 300_000; // 5 minutes

    /// <summary>Nombre maximum de fichiers de contexte.</summary>
    public int ContextFileLimit { get; set; } = 10;

    /// <summary>Température pour la génération (0.0 - 1.0).</summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>Activer le mode debug pour les logs détaillés.</summary>
    public bool DebugMode { get; set; } = false;
}