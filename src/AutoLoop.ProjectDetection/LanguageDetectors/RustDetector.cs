using AutoLoop.Core.Models;

namespace AutoLoop.ProjectDetection.LanguageDetectors;

/// <summary>
/// Détecteur pour les projets Rust.
/// </summary>
public sealed class RustDetector : ILanguageDetector
{
    private static readonly string[] ConfigFiles = ["Cargo.toml", "Cargo.lock"];

    public Task<ProjectInfo?> DetectAsync(string projectPath, CancellationToken ct = default)
    {
        // Vérifier Cargo.toml
        var cargoTomlPath = Path.Combine(projectPath, "Cargo.toml");
        if (!File.Exists(cargoTomlPath))
            return Task.FromResult<ProjectInfo?>(null);

        // Vérifier src/lib.rs ou src/main.rs
        var hasSrc = Directory.Exists(Path.Combine(projectPath, "src"));

        // Détecter le type de projet (binaire vs library)
        var isBinary = File.Exists(Path.Combine(projectPath, "src", "main.rs"));
        var isLibrary = File.Exists(Path.Combine(projectPath, "src", "lib.rs"));

        // Détecter le framework de test (Rust a des tests intégrés)
        var testCommand = "cargo test";
        var testFramework = "built-in";

        return Task.FromResult<ProjectInfo?>(new ProjectInfo
        {
            ProjectPath = projectPath,
            Type = ProjectType.Rust,
            Language = "Rust",
            Framework = isBinary ? "binary" : isLibrary ? "library" : null,
            PackageManager = "cargo",
            TestCommand = testCommand,
            BuildCommand = "cargo build",
            SourcePatterns = ["src/**/*.rs"],
            ConfigFiles = ConfigFiles.Where(f => File.Exists(Path.Combine(projectPath, f))).ToList(),
            Metadata = new Dictionary<string, object>
            {
                ["testFramework"] = testFramework,
                ["isBinary"] = isBinary,
                ["isLibrary"] = isLibrary
            }
        });
    }
}