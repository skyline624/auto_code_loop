using System.Text.Json;
using AutoLoop.Core.Interfaces;
using AutoLoop.Core.Models;
using AutoLoop.ProjectDetection.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoLoop.ProjectDetection;

/// <summary>
/// Détecteur de projet qui analyse les fichiers pour déterminer le type de projet.
/// </summary>
public sealed class ProjectDetector : IProjectDetector
{
    private readonly ILogger<ProjectDetector> _logger;
    private readonly ProjectDetectionOptions _options;
    private readonly IEnumerable<ILanguageDetector> _languageDetectors;

    // Patterns de fichiers sources par type de projet
    private static readonly Dictionary<ProjectType, string[]> SourcePatterns = new()
    {
        [ProjectType.NodeJs] = ["src/**/*.ts", "src/**/*.js", "lib/**/*.js", "**/*.tsx", "**/*.jsx"],
        [ProjectType.Python] = ["**/*.py", "src/**/*.py", "app/**/*.py"],
        [ProjectType.DotNet] = ["**/*.cs", "src/**/*.cs", "**/*.fs", "**/*.vb"],
        [ProjectType.Rust] = ["src/**/*.rs"],
        [ProjectType.Go] = ["**/*.go"],
        [ProjectType.Java] = ["src/main/java/**/*.java", "src/**/*.java"],
        [ProjectType.Ruby] = ["lib/**/*.rb", "app/**/*.rb", "**/*.rb"],
        [ProjectType.Php] = ["src/**/*.php", "app/**/*.php", "**/*.php"]
    };

    // Commandes de test par type de projet
    private static readonly Dictionary<ProjectType, string[]> TestCommands = new()
    {
        [ProjectType.NodeJs] = ["npm test", "yarn test", "pnpm test"],
        [ProjectType.Python] = ["pytest", "python -m pytest", "python -m unittest"],
        [ProjectType.DotNet] = ["dotnet test"],
        [ProjectType.Rust] = ["cargo test"],
        [ProjectType.Go] = ["go test ./..."],
        [ProjectType.Java] = ["mvn test", "gradle test"],
        [ProjectType.Ruby] = ["bundle exec rspec", "ruby -Ilib -e 'Dir.glob(\"test/**/*_test.rb\").each { |f| require f }'"],
        [ProjectType.Php] = ["phpunit", "vendor/bin/phpunit"]
    };

    // Commandes de build par type de projet
    private static readonly Dictionary<ProjectType, string[]> BuildCommands = new()
    {
        [ProjectType.NodeJs] = ["npm run build", "yarn build", "pnpm build"],
        [ProjectType.Python] = ["pip install -e .", "poetry build"],
        [ProjectType.DotNet] = ["dotnet build"],
        [ProjectType.Rust] = ["cargo build"],
        [ProjectType.Go] = ["go build ./..."],
        [ProjectType.Java] = ["mvn compile", "gradle build"],
        [ProjectType.Ruby] = ["bundle install"],
        [ProjectType.Php] = ["composer install"]
    };

    public ProjectDetector(
        IOptions<ProjectDetectionOptions> options,
        IEnumerable<ILanguageDetector> languageDetectors,
        ILogger<ProjectDetector> logger)
    {
        _options = options.Value;
        _languageDetectors = languageDetectors;
        _logger = logger;
    }

    public async Task<ProjectInfo> DetectAsync(string projectPath, CancellationToken ct = default)
    {
        _logger.LogDebug("Détection du type de projet pour: {ProjectPath}", projectPath);

        if (!Directory.Exists(projectPath))
        {
            throw new DirectoryNotFoundException($"Le répertoire n'existe pas: {projectPath}");
        }

        // Essayer chaque détecteur de langue
        foreach (var detector in _languageDetectors)
        {
            var detected = await detector.DetectAsync(projectPath, ct);
            if (detected != null)
            {
                _logger.LogInformation("Projet détecté: {Type} ({Language})",
                    detected.Type, detected.Language);

                return EnrichProjectInfo(detected, projectPath);
            }
        }

        // Fallback: détection par fichiers
        var fallbackInfo = await DetectByFilesAsync(projectPath, ct);
        if (fallbackInfo != null)
        {
            _logger.LogInformation("Projet détecté (fallback): {Type}", fallbackInfo.Type);
            return EnrichProjectInfo(fallbackInfo, projectPath);
        }

        // Aucun type détecté
        _logger.LogWarning("Impossible de détecter le type de projet. Utilisation de Unknown.");
        return new ProjectInfo
        {
            ProjectPath = projectPath,
            Type = ProjectType.Unknown,
            SourcePatterns = []
        };
    }

    public async Task<bool> DetectTestFrameworkAsync(string projectPath, CancellationToken ct = default)
    {
        var info = await DetectAsync(projectPath, ct);
        return !string.IsNullOrEmpty(info.TestCommand);
    }

    private async Task<ProjectInfo?> DetectByFilesAsync(string projectPath, CancellationToken ct)
    {
        // Vérifier les fichiers caractéristiques
        var detectionFiles = new (string Pattern, ProjectType Type, string? Language)[]
        {
            ("package.json", ProjectType.NodeJs, null),
            ("pyproject.toml", ProjectType.Python, null),
            ("requirements.txt", ProjectType.Python, "Python"),
            ("setup.py", ProjectType.Python, "Python"),
            ("*.csproj", ProjectType.DotNet, "C#"),
            ("Cargo.toml", ProjectType.Rust, "Rust"),
            ("go.mod", ProjectType.Go, "Go"),
            ("pom.xml", ProjectType.Java, "Java"),
            ("build.gradle*", ProjectType.Java, "Java"),
            ("Gemfile", ProjectType.Ruby, "Ruby"),
            ("composer.json", ProjectType.Php, "PHP")
        };

        foreach (var (pattern, type, language) in detectionFiles)
        {
            var files = pattern.Contains('*')
                ? Directory.GetFiles(projectPath, pattern, SearchOption.TopDirectoryOnly)
                : File.Exists(Path.Combine(projectPath, pattern)) ? new[] { pattern } : Array.Empty<string>();

            if (files.Length > 0)
            {
                return new ProjectInfo
                {
                    ProjectPath = projectPath,
                    Type = type,
                    Language = language ?? type.ToString(),
                    PackageManager = await DetectPackageManagerAsync(projectPath, type),
                    TestCommand = GetTestCommand(projectPath, type),
                    BuildCommand = GetBuildCommand(projectPath, type),
                    SourcePatterns = SourcePatterns.GetValueOrDefault(type, [])
                };
            }
        }

        return null;
    }

    private ProjectInfo EnrichProjectInfo(ProjectInfo info, string projectPath)
    {
        // Ajouter les patterns source si non définis
        if (info.SourcePatterns.Count == 0)
        {
            info = info with
            {
                SourcePatterns = SourcePatterns.GetValueOrDefault(info.Type, [])
            };
        }

        // Ajouter la commande de test si non définie
        if (string.IsNullOrEmpty(info.TestCommand))
        {
            info = info with
            {
                TestCommand = GetTestCommand(projectPath, info.Type)
            };
        }

        // Ajouter la commande de build si non définie
        if (string.IsNullOrEmpty(info.BuildCommand))
        {
            info = info with
            {
                BuildCommand = GetBuildCommand(projectPath, info.Type)
            };
        }

        return info;
    }

    private Task<string?> DetectPackageManagerAsync(string projectPath, ProjectType type)
    {
        string? result = type switch
        {
            ProjectType.NodeJs when File.Exists(Path.Combine(projectPath, "yarn.lock")) => "yarn",
            ProjectType.NodeJs when File.Exists(Path.Combine(projectPath, "pnpm-lock.yaml")) => "pnpm",
            ProjectType.NodeJs when File.Exists(Path.Combine(projectPath, "package-lock.json")) => "npm",
            ProjectType.Python when File.Exists(Path.Combine(projectPath, "poetry.lock")) => "poetry",
            ProjectType.Python when File.Exists(Path.Combine(projectPath, "Pipfile.lock")) => "pipenv",
            ProjectType.Python => "pip",
            ProjectType.DotNet => "nuget",
            ProjectType.Rust => "cargo",
            ProjectType.Go => "go mod",
            ProjectType.Java when File.Exists(Path.Combine(projectPath, "pom.xml")) => "maven",
            ProjectType.Java => "gradle",
            ProjectType.Ruby => "bundler",
            ProjectType.Php => "composer",
            _ => null
        };
        return Task.FromResult(result);
    }

    private string? GetTestCommand(string projectPath, ProjectType type)
    {
        var commands = TestCommands.GetValueOrDefault(type);
        if (commands == null || commands.Length == 0)
            return null;

        // Pour Node.js, vérifier le script dans package.json
        if (type == ProjectType.NodeJs)
        {
            try
            {
                var packageJsonPath = Path.Combine(projectPath, "package.json");
                if (File.Exists(packageJsonPath))
                {
                    var json = File.ReadAllText(packageJsonPath);
                    var packageJson = JsonDocument.Parse(json);
                    if (packageJson.RootElement.TryGetProperty("scripts", out var scripts)
                        && scripts.TryGetProperty("test", out _))
                    {
                        return "npm test";
                    }
                }
            }
            catch
            {
                // Ignorer les erreurs de parsing
            }
        }

        return commands[0];
    }

    private string? GetBuildCommand(string projectPath, ProjectType type)
    {
        var commands = BuildCommands.GetValueOrDefault(type);
        return commands?.FirstOrDefault();
    }
}