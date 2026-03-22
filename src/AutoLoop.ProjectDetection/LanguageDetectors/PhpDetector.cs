using AutoLoop.Core.Models;
using System.Text.Json;

namespace AutoLoop.ProjectDetection.LanguageDetectors;

/// <summary>
/// Détecteur pour les projets PHP.
/// </summary>
public sealed class PhpDetector : ILanguageDetector
{
    private static readonly string[] ConfigFiles = ["composer.json", "composer.lock"];

    public Task<ProjectInfo?> DetectAsync(string projectPath, CancellationToken ct = default)
    {
        // Vérifier composer.json (marqueur principal d'un projet PHP moderne)
        var composerPath = Path.Combine(projectPath, "composer.json");
        if (!File.Exists(composerPath))
            return Task.FromResult<ProjectInfo?>(null);

        var framework = TryDetectFramework(composerPath);
        var testFramework = TryDetectTestFramework(composerPath);
        var testCommand = testFramework == "phpunit"
            ? "vendor/bin/phpunit"
            : "vendor/bin/pest";

        return Task.FromResult<ProjectInfo?>(new ProjectInfo
        {
            ProjectPath = projectPath,
            Type = ProjectType.Php,
            Language = "PHP",
            Framework = framework,
            PackageManager = "composer",
            TestCommand = testCommand,
            BuildCommand = "composer install",
            SourcePatterns = ["src/**/*.php", "app/**/*.php", "**/*.php"],
            ConfigFiles = ConfigFiles.Where(f => File.Exists(Path.Combine(projectPath, f))).ToList(),
            Metadata = new Dictionary<string, object>
            {
                ["testFramework"] = testFramework ?? "phpunit"
            }
        });
    }

    private static string? TryDetectFramework(string composerPath)
    {
        try
        {
            var json = File.ReadAllText(composerPath).ToLowerInvariant();
            if (json.Contains("laravel/framework"))  return "laravel";
            if (json.Contains("symfony/symfony") || json.Contains("symfony/framework-bundle")) return "symfony";
            if (json.Contains("slim/slim"))           return "slim";
            if (json.Contains("cakephp/cakephp"))     return "cakephp";
            if (json.Contains("codeigniter4"))         return "codeigniter";
        }
        catch { }
        return null;
    }

    private static string? TryDetectTestFramework(string composerPath)
    {
        try
        {
            var json = File.ReadAllText(composerPath).ToLowerInvariant();
            if (json.Contains("pestphp/pest"))  return "pest";
            if (json.Contains("phpunit/phpunit")) return "phpunit";
        }
        catch { }
        return "phpunit"; // défaut pour PHP
    }
}
