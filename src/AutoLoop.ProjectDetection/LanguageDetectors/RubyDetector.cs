using AutoLoop.Core.Models;

namespace AutoLoop.ProjectDetection.LanguageDetectors;

/// <summary>
/// Détecteur pour les projets Ruby.
/// </summary>
public sealed class RubyDetector : ILanguageDetector
{
    private static readonly string[] ConfigFiles = ["Gemfile", "Gemfile.lock", ".ruby-version"];

    public Task<ProjectInfo?> DetectAsync(string projectPath, CancellationToken ct = default)
    {
        // Vérifier Gemfile (marqueur principal d'un projet Ruby)
        var gemfilePath = Path.Combine(projectPath, "Gemfile");
        if (!File.Exists(gemfilePath))
            return Task.FromResult<ProjectInfo?>(null);

        // Détecter le framework applicatif
        var hasRailsGemfile = TryDetectFramework(gemfilePath, out var framework);
        var hasSinatra = framework == "sinatra";
        var isRails = framework == "rails";

        // Détecter le framework de test
        var testFramework = DetectTestFramework(gemfilePath);
        var testCommand = testFramework == "rspec" ? "bundle exec rspec" : "bundle exec rake test";

        return Task.FromResult<ProjectInfo?>(new ProjectInfo
        {
            ProjectPath = projectPath,
            Type = ProjectType.Ruby,
            Language = "Ruby",
            Framework = framework,
            PackageManager = "bundler",
            TestCommand = testCommand,
            BuildCommand = "bundle install",
            SourcePatterns = ["lib/**/*.rb", "app/**/*.rb", "**/*.rb"],
            ConfigFiles = ConfigFiles.Where(f => File.Exists(Path.Combine(projectPath, f))).ToList(),
            Metadata = new Dictionary<string, object>
            {
                ["testFramework"] = testFramework,
                ["isRails"] = isRails,
                ["hasSinatra"] = hasSinatra
            }
        });
    }

    private static bool TryDetectFramework(string gemfilePath, out string? framework)
    {
        framework = null;
        try
        {
            var content = File.ReadAllText(gemfilePath).ToLowerInvariant();
            if (content.Contains("'rails'") || content.Contains("\"rails\""))
                framework = "rails";
            else if (content.Contains("'sinatra'") || content.Contains("\"sinatra\""))
                framework = "sinatra";
            else if (content.Contains("'hanami'") || content.Contains("\"hanami\""))
                framework = "hanami";
            return framework != null;
        }
        catch
        {
            return false;
        }
    }

    private static string DetectTestFramework(string gemfilePath)
    {
        try
        {
            var content = File.ReadAllText(gemfilePath).ToLowerInvariant();
            if (content.Contains("'rspec'") || content.Contains("\"rspec\"") ||
                content.Contains("rspec-rails"))
                return "rspec";
            if (content.Contains("'minitest'") || content.Contains("\"minitest\""))
                return "minitest";
        }
        catch { }
        return "rspec"; // défaut pour Ruby
    }
}
