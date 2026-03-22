using System.Text.Json;
using AutoLoop.Core.Models;

namespace AutoLoop.ProjectDetection.LanguageDetectors;

/// <summary>
/// Détecteur pour les projets Node.js / JavaScript / TypeScript.
/// </summary>
public sealed class NodeJsDetector : ILanguageDetector
{
    private static readonly string[] ConfigFiles = ["package.json", "tsconfig.json", "jsconfig.json"];

    public async Task<ProjectInfo?> DetectAsync(string projectPath, CancellationToken ct = default)
    {
        var packageJsonPath = Path.Combine(projectPath, "package.json");
        if (!File.Exists(packageJsonPath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(packageJsonPath, ct);
            var packageJson = JsonDocument.Parse(json);

            var language = "JavaScript";
            var framework = DetectFramework(packageJson);
            var isTypeScript = File.Exists(Path.Combine(projectPath, "tsconfig.json"));

            if (isTypeScript)
                language = "TypeScript";

            // Détecter le gestionnaire de paquets
            var packageManager = DetectPackageManager(projectPath);

            // Détecter les scripts de test
            var testCommand = DetectTestCommand(packageJson);

            // Détecter les frameworks de test
            var testFramework = DetectTestFramework(packageJson);

            return new ProjectInfo
            {
                ProjectPath = projectPath,
                Type = ProjectType.NodeJs,
                Language = language,
                Framework = framework,
                PackageManager = packageManager,
                TestCommand = testCommand,
                BuildCommand = DetectBuildCommand(packageJson),
                SourcePatterns = ["src/**/*.ts", "src/**/*.js", "lib/**/*.js", "**/*.tsx", "**/*.jsx"],
                ConfigFiles = ConfigFiles.ToList(),
                Metadata = new Dictionary<string, object>
                {
                    ["testFramework"] = testFramework,
                    ["isTypeScript"] = isTypeScript
                }
            };
        }
        catch (Exception)
        {
            // Si on ne peut pas parser package.json, on retourne quand même Node.js
            return new ProjectInfo
            {
                ProjectPath = projectPath,
                Type = ProjectType.NodeJs,
                Language = "JavaScript",
                PackageManager = "npm",
                TestCommand = "npm test",
                SourcePatterns = ["src/**/*.js", "**/*.jsx"],
                ConfigFiles = ConfigFiles.ToList()
            };
        }
    }

    private static string? DetectFramework(JsonDocument packageJson)
    {
        if (!packageJson.RootElement.TryGetProperty("dependencies", out var deps))
            return null;

        var depsDict = deps.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());

        if (depsDict.ContainsKey("react"))
            return "React";
        if (depsDict.ContainsKey("vue"))
            return "Vue";
        if (depsDict.ContainsKey("angular") || depsDict.ContainsKey("@angular/core"))
            return "Angular";
        if (depsDict.ContainsKey("svelte"))
            return "Svelte";
        if (depsDict.ContainsKey("next"))
            return "Next.js";
        if (depsDict.ContainsKey("nuxt"))
            return "Nuxt";
        if (depsDict.ContainsKey("express"))
            return "Express";
        if (depsDict.ContainsKey("fastify"))
            return "Fastify";
        if (depsDict.ContainsKey("nest"))
            return "NestJS";

        return null;
    }

    private static string DetectPackageManager(string projectPath)
    {
        if (File.Exists(Path.Combine(projectPath, "yarn.lock")))
            return "yarn";
        if (File.Exists(Path.Combine(projectPath, "pnpm-lock.yaml")))
            return "pnpm";
        return "npm";
    }

    private static string? DetectTestCommand(JsonDocument packageJson)
    {
        if (packageJson.RootElement.TryGetProperty("scripts", out var scripts)
            && scripts.TryGetProperty("test", out _))
        {
            return "npm test";
        }
        return null;
    }

    private static string? DetectBuildCommand(JsonDocument packageJson)
    {
        if (packageJson.RootElement.TryGetProperty("scripts", out var scripts)
            && scripts.TryGetProperty("build", out _))
        {
            return "npm run build";
        }
        return null;
    }

    private static string DetectTestFramework(JsonDocument packageJson)
    {
        if (!packageJson.RootElement.TryGetProperty("devDependencies", out var deps))
            return "unknown";

        var depsDict = deps.EnumerateObject().Select(p => p.Name).ToList();

        if (depsDict.Contains("jest"))
            return "jest";
        if (depsDict.Contains("vitest"))
            return "vitest";
        if (depsDict.Contains("mocha"))
            return "mocha";
        if (depsDict.Contains("jasmine"))
            return "jasmine";
        if (depsDict.Contains("@testing-library/react"))
            return "testing-library";

        return "unknown";
    }
}