namespace AutoLoop.Core.Models;

/// <summary>
/// Informations sur le projet cible détecté.
/// Permet de supporter plusieurs langages et frameworks.
/// </summary>
public sealed record ProjectInfo
{
    /// <summary>Chemin absolu vers la racine du projet.</summary>
    public required string ProjectPath { get; init; }

    /// <summary>Type de projet détecté.</summary>
    public required ProjectType Type { get; init; }

    /// <summary>Langage principal (JavaScript, TypeScript, Python, C#, Rust, Go, Java, Ruby).</summary>
    public string? Language { get; init; }

    /// <summary>Framework détecté (React, Vue, Django, FastAPI, ASP.NET, Express, etc.).</summary>
    public string? Framework { get; init; }

    /// <summary>Gestionnaire de paquets (npm, yarn, pnpm, pip, poetry, nuget, cargo, go mod).</summary>
    public string? PackageManager { get; init; }

    /// <summary>Commande pour exécuter les tests (npm test, pytest, dotnet test, cargo test, etc.).</summary>
    public string? TestCommand { get; init; }

    /// <summary>Commande pour builder le projet.</summary>
    public string? BuildCommand { get; init; }

    /// <summary>Patterns de fichiers sources (glob patterns).</summary>
    public IReadOnlyList<string> SourcePatterns { get; init; } = [];

    /// <summary>Fichiers de configuration détectés.</summary>
    public IReadOnlyList<string> ConfigFiles { get; init; } = [];

    /// <summary>Métadonnées additionnelles spécifiques au langage.</summary>
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// Types de projets supportés.
/// </summary>
public enum ProjectType
{
    Unknown,
    NodeJs,
    Python,
    DotNet,
    Rust,
    Go,
    Java,
    Ruby,
    Php
}