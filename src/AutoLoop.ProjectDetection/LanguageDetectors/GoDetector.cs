using AutoLoop.Core.Models;

namespace AutoLoop.ProjectDetection.LanguageDetectors;

/// <summary>
/// Détecteur pour les projets Go.
/// </summary>
public sealed class GoDetector : ILanguageDetector
{
    private static readonly string[] ConfigFiles = ["go.mod", "go.sum"];

    public Task<ProjectInfo?> DetectAsync(string projectPath, CancellationToken ct = default)
    {
        // Vérifier go.mod
        var goModPath = Path.Combine(projectPath, "go.mod");
        if (!File.Exists(goModPath))
            return Task.FromResult<ProjectInfo?>(null);

        // Détecter le module Go
        var moduleName = TryDetectModuleName(goModPath);

        // Vérifier les patterns de sources
        var hasInternal = Directory.Exists(Path.Combine(projectPath, "internal"));
        var hasPkg = Directory.Exists(Path.Combine(projectPath, "pkg"));
        var hasCmd = Directory.Exists(Path.Combine(projectPath, "cmd"));

        // Framework de test intégré
        var testCommand = "go test ./...";
        var testFramework = "built-in";

        return Task.FromResult<ProjectInfo?>(new ProjectInfo
        {
            ProjectPath = projectPath,
            Type = ProjectType.Go,
            Language = "Go",
            Framework = hasCmd ? "cli" : hasInternal ? "library" : null,
            PackageManager = "go mod",
            TestCommand = testCommand,
            BuildCommand = "go build ./...",
            SourcePatterns = ["**/*.go"],
            ConfigFiles = ConfigFiles.Where(f => File.Exists(Path.Combine(projectPath, f))).ToList(),
            Metadata = new Dictionary<string, object>
            {
                ["testFramework"] = testFramework,
                ["moduleName"] = moduleName ?? ""
            }
        });
    }

    private static string? TryDetectModuleName(string goModPath)
    {
        try
        {
            var lines = File.ReadAllLines(goModPath);
            foreach (var line in lines)
            {
                if (line.StartsWith("module "))
                {
                    return line.Substring(7).Trim();
                }
            }
        }
        catch
        {
            // Ignorer les erreurs
        }
        return null;
    }
}