namespace AutoLoop.ProjectDetection.Options;

/// <summary>
/// Options pour la détection de projet.
/// </summary>
public sealed class ProjectDetectionOptions
{
    public const string Section = "ProjectDetection";

    /// <summary>Détecter automatiquement le type de projet.</summary>
    public bool AutoDetect { get; set; } = true;

    /// <summary>Patterns personnalisés pour la détection.</summary>
    public Dictionary<string, string> CustomPatterns { get; set; } = new();

    /// <summary>Timeout pour la détection (ms).</summary>
    public int DetectionTimeoutMs { get; set; } = 5000;
}