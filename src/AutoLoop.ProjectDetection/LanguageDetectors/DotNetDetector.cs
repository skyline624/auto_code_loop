using AutoLoop.Core.Models;

namespace AutoLoop.ProjectDetection.LanguageDetectors;

/// <summary>
/// Détecteur pour les projets .NET (C#, F#, VB.NET).
/// </summary>
public sealed class DotNetDetector : ILanguageDetector
{
    private static readonly string[] ConfigFiles = ["*.csproj", "*.fsproj", "*.vbproj", "Directory.Build.props", "Directory.Packages.props", "global.json"];

    public Task<ProjectInfo?> DetectAsync(string projectPath, CancellationToken ct = default)
    {
        // Vérifier les fichiers projet
        var projectFiles = Directory.GetFiles(projectPath, "*.csproj", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(projectPath, "*.fsproj", SearchOption.TopDirectoryOnly))
            .Concat(Directory.GetFiles(projectPath, "*.vbproj", SearchOption.TopDirectoryOnly))
            .ToList();

        // Vérifier global.json ou Directory.Build.props
        var hasGlobalJson = File.Exists(Path.Combine(projectPath, "global.json"));
        var hasDirectoryBuildProps = File.Exists(Path.Combine(projectPath, "Directory.Build.props"));

        if (projectFiles.Count == 0 && !hasGlobalJson && !hasDirectoryBuildProps)
        {
            // Vérifier dans les sous-répertoires
            projectFiles = Directory.GetFiles(projectPath, "*.csproj", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(projectPath, "*.fsproj", SearchOption.AllDirectories))
                .Take(5)
                .ToList();

            if (projectFiles.Count == 0)
                return Task.FromResult<ProjectInfo?>(null);
        }

        // Détecter le langage principal
        var language = DetectLanguage(projectPath, projectFiles);

        // Détecter le framework
        var framework = DetectFramework(projectPath);

        // Détecter les frameworks de test
        var (testFramework, testCommand) = DetectTestFramework(projectPath);

        return Task.FromResult<ProjectInfo?>(new ProjectInfo
        {
            ProjectPath = projectPath,
            Type = ProjectType.DotNet,
            Language = language,
            Framework = framework,
            PackageManager = "nuget",
            TestCommand = testCommand ?? "dotnet test",
            BuildCommand = "dotnet build",
            SourcePatterns = ["src/**/*.cs", "**/*.cs", "src/**/*.fs", "**/*.fs"],
            ConfigFiles = ConfigFiles.Where(f =>
                f.Contains("*")
                    ? Directory.GetFiles(projectPath, f, SearchOption.TopDirectoryOnly).Length > 0
                    : File.Exists(Path.Combine(projectPath, f))).ToList(),
            Metadata = new Dictionary<string, object>
            {
                ["testFramework"] = testFramework,
                ["projectFiles"] = projectFiles.Take(5).ToList()
            }
        });
    }

    private static string DetectLanguage(string projectPath, List<string> projectFiles)
    {
        var csprojCount = projectFiles.Count(f => f.EndsWith(".csproj"));
        var fsprojCount = projectFiles.Count(f => f.EndsWith(".fsproj"));
        var vbprojCount = projectFiles.Count(f => f.EndsWith(".vbproj"));

        if (fsprojCount > csprojCount && fsprojCount > vbprojCount)
            return "F#";
        if (vbprojCount > csprojCount && vbprojCount > fsprojCount)
            return "VB.NET";
        return "C#";
    }

    private static string? DetectFramework(string projectPath)
    {
        // Vérifier les fichiers projet pour détecter le framework
        var projectFiles = Directory.GetFiles(projectPath, "*.csproj", SearchOption.AllDirectories)
            .Take(1)
            .ToList();

        if (projectFiles.Count == 0)
            return null;

        try
        {
            var content = File.ReadAllText(projectFiles[0]);

            if (content.Contains("Microsoft.NET.Sdk.Web"))
                return "ASP.NET Core";
            if (content.Contains("Microsoft.NET.Sdk.Worker"))
                return ".NET Worker Service";
            if (content.Contains("Microsoft.NET.Sdk.BlazorWebAssembly"))
                return "Blazor WebAssembly";
            if (content.Contains("Microsoft.NET.Sdk.Razor"))
                return "Blazor Server";
            if (content.Contains("Microsoft.NET.Sdk.WindowsDesktop"))
                return "Windows Desktop";
        }
        catch
        {
            // Ignorer les erreurs de lecture
        }

        return null;
    }

    private static (string testFramework, string? testCommand) DetectTestFramework(string projectPath)
    {
        // Vérifier les packages de test dans les fichiers projet
        var allProjectFiles = Directory.GetFiles(projectPath, "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(projectPath, "*.fsproj", SearchOption.AllDirectories));

        foreach (var file in allProjectFiles.Take(5))
        {
            try
            {
                var content = File.ReadAllText(file);

                if (content.Contains("xunit"))
                    return ("xunit", "dotnet test");
                if (content.Contains("NUnit"))
                    return ("NUnit", "dotnet test");
                if (content.Contains("Microsoft.NET.Test.Sdk") && content.Contains("MSTest"))
                    return ("MSTest", "dotnet test");
            }
            catch
            {
                // Ignorer les erreurs de lecture
            }
        }

        return ("dotnet", "dotnet test");
    }
}