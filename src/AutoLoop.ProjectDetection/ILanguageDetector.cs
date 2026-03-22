using AutoLoop.Core.Models;

namespace AutoLoop.ProjectDetection;

/// <summary>
/// Interface pour les détecteurs de langage spécifiques.
/// </summary>
public interface ILanguageDetector
{
    /// <summary>
    /// Détecte si le projet est du type géré par ce détecteur.
    /// </summary>
    /// <param name="projectPath">Chemin du projet</param>
    /// <param name="ct">Token d'annulation</param>
    /// <returns>ProjectInfo si détecté, null sinon</returns>
    Task<ProjectInfo?> DetectAsync(string projectPath, CancellationToken ct = default);
}