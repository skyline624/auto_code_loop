using System.Text.Json;
using AutoLoop.Core.Models;

namespace AutoLoop.ProjectDetection.LanguageDetectors;

/// <summary>
/// Détecteur pour les projets Python.
/// </summary>
public sealed class PythonDetector : ILanguageDetector
{
    private static readonly string[] ConfigFiles = ["pyproject.toml", "setup.py", "setup.cfg", "requirements.txt", "Pipfile", "poetry.lock"];

    public async Task<ProjectInfo?> DetectAsync(string projectPath, CancellationToken ct = default)
    {
        // Vérifier les fichiers de configuration Python
        var hasConfig = ConfigFiles.Any(f => File.Exists(Path.Combine(projectPath, f)));
        var hasPyFiles = Directory.GetFiles(projectPath, "*.py", SearchOption.TopDirectoryOnly).Length > 0 ||
                         Directory.GetDirectories(projectPath, "*").Any(d => Directory.GetFiles(d, "*.py").Length > 0);

        if (!hasConfig && !hasPyFiles)
            return null;

        // Détecter le framework et le gestionnaire de paquets
        var (packageManager, framework) = await DetectPackageManagerAndFrameworkAsync(projectPath, ct);

        // Détecter le framework de test
        var (testFramework, testCommand) = DetectTestFramework(projectPath);

        // Détecter les patterns de sources
        var sourcePatterns = DetectSourcePatterns(projectPath);

        return new ProjectInfo
        {
            ProjectPath = projectPath,
            Type = ProjectType.Python,
            Language = "Python",
            Framework = framework,
            PackageManager = packageManager,
            TestCommand = testCommand,
            BuildCommand = packageManager == "poetry" ? "poetry build" : null,
            SourcePatterns = sourcePatterns,
            ConfigFiles = ConfigFiles.Where(f => File.Exists(Path.Combine(projectPath, f))).ToList(),
            Metadata = new Dictionary<string, object>
            {
                ["testFramework"] = testFramework
            }
        };
    }

    private async Task<(string packageManager, string? framework)> DetectPackageManagerAndFrameworkAsync(
        string projectPath, CancellationToken ct)
    {
        // Poetry
        if (File.Exists(Path.Combine(projectPath, "pyproject.toml")))
        {
            try
            {
                var content = await File.ReadAllTextAsync(Path.Combine(projectPath, "pyproject.toml"), ct);
                var framework = DetectFrameworkFromPyproject(content);
                return ("poetry", framework);
            }
            catch
            {
                return ("poetry", null);
            }
        }

        // Pipenv
        if (File.Exists(Path.Combine(projectPath, "Pipfile")))
            return ("pipenv", null);

        // setup.py
        if (File.Exists(Path.Combine(projectPath, "setup.py")))
            return ("setuptools", null);

        // requirements.txt
        if (File.Exists(Path.Combine(projectPath, "requirements.txt")))
            return ("pip", null);

        return ("pip", null);
    }

    private static string? DetectFrameworkFromPyproject(string content)
    {
        if (content.Contains("django", StringComparison.OrdinalIgnoreCase))
            return "Django";
        if (content.Contains("fastapi", StringComparison.OrdinalIgnoreCase))
            return "FastAPI";
        if (content.Contains("flask", StringComparison.OrdinalIgnoreCase))
            return "Flask";
        if (content.Contains("flask", StringComparison.OrdinalIgnoreCase))
            return "Flask";
        if (content.Contains("pydantic", StringComparison.OrdinalIgnoreCase))
            return "Pydantic";
        return null;
    }

    private static (string testFramework, string? testCommand) DetectTestFramework(string projectPath)
    {
        // Vérifier pytest
        var pytestIni = Path.Combine(projectPath, "pytest.ini");
        var pyproject = Path.Combine(projectPath, "pyproject.toml");

        if (File.Exists(pytestIni) || (File.Exists(pyproject) && File.ReadAllText(pyproject).Contains("pytest")))
        {
            return ("pytest", "pytest");
        }

        // Vérifier unittest
        if (Directory.GetFiles(projectPath, "test_*.py", SearchOption.AllDirectories).Length > 0 ||
            Directory.GetFiles(projectPath, "*_test.py", SearchOption.AllDirectories).Length > 0)
        {
            return ("unittest", "python -m pytest");
        }

        return ("unknown", "python -m pytest");
    }

    private static string[] DetectSourcePatterns(string projectPath)
    {
        var patterns = new List<string> { "**/*.py" };

        // Vérifier les structures de projet courantes
        if (Directory.Exists(Path.Combine(projectPath, "src")))
            patterns.Insert(0, "src/**/*.py");

        if (Directory.Exists(Path.Combine(projectPath, "app")))
            patterns.Insert(0, "app/**/*.py");

        return patterns.ToArray();
    }
}