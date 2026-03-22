using AutoLoop.Core.Models;

namespace AutoLoop.ProjectDetection.LanguageDetectors;

/// <summary>
/// Détecteur pour les projets Java.
/// </summary>
public sealed class JavaDetector : ILanguageDetector
{
    private static readonly string[] ConfigFiles = ["pom.xml", "build.gradle", "build.gradle.kts", "settings.gradle", "settings.gradle.kts"];

    public async Task<ProjectInfo?> DetectAsync(string projectPath, CancellationToken ct = default)
    {
        // Vérifier pom.xml (Maven) ou build.gradle (Gradle)
        var pomXmlPath = Path.Combine(projectPath, "pom.xml");
        var buildGradlePath = Path.Combine(projectPath, "build.gradle");
        var buildGradleKtsPath = Path.Combine(projectPath, "build.gradle.kts");

        var isMaven = File.Exists(pomXmlPath);
        var isGradle = File.Exists(buildGradlePath) || File.Exists(buildGradleKtsPath);

        if (!isMaven && !isGradle)
            return null;

        // Vérifier la structure de projet standard
        var hasSrcMainJava = Directory.Exists(Path.Combine(projectPath, "src", "main", "java"));
        var hasSrcTestJava = Directory.Exists(Path.Combine(projectPath, "src", "test", "java"));

        // Détecter le framework
        var framework = await DetectFrameworkAsync(projectPath, isMaven);

        // Détecter le framework de test
        var (testFramework, testCommand) = DetectTestFramework(projectPath, isMaven);

        var packageManager = isMaven ? "maven" : "gradle";
        var buildCommand = isMaven ? "mvn compile" : "gradle build";

        return new ProjectInfo
        {
            ProjectPath = projectPath,
            Type = ProjectType.Java,
            Language = "Java",
            Framework = framework,
            PackageManager = packageManager,
            TestCommand = testCommand ?? (isMaven ? "mvn test" : "gradle test"),
            BuildCommand = buildCommand,
            SourcePatterns = ["src/main/java/**/*.java", "src/**/*.java"],
            ConfigFiles = ConfigFiles.Where(f => File.Exists(Path.Combine(projectPath, f))).ToList(),
            Metadata = new Dictionary<string, object>
            {
                ["testFramework"] = testFramework,
                ["isMaven"] = isMaven,
                ["isGradle"] = isGradle,
                ["hasStandardStructure"] = hasSrcMainJava
            }
        };
    }

    private async Task<string?> DetectFrameworkAsync(string projectPath, bool isMaven)
    {
        try
        {
            if (isMaven)
            {
                var content = await File.ReadAllTextAsync(Path.Combine(projectPath, "pom.xml"));

                if (content.Contains("spring-boot", StringComparison.OrdinalIgnoreCase))
                    return "Spring Boot";
                if (content.Contains("spring-framework", StringComparison.OrdinalIgnoreCase) || content.Contains("org.springframework", StringComparison.OrdinalIgnoreCase))
                    return "Spring Framework";
                if (content.Contains("jakarta", StringComparison.OrdinalIgnoreCase))
                    return "Jakarta EE";
                if (content.Contains("quarkus", StringComparison.OrdinalIgnoreCase))
                    return "Quarkus";
                if (content.Contains("micronaut", StringComparison.OrdinalIgnoreCase))
                    return "Micronaut";
            }
            else
            {
                var buildFile = Path.Combine(projectPath, "build.gradle.kts");
                if (!File.Exists(buildFile))
                    buildFile = Path.Combine(projectPath, "build.gradle");

                if (File.Exists(buildFile))
                {
                    var content = await File.ReadAllTextAsync(buildFile);

                    if (content.Contains("spring-boot", StringComparison.OrdinalIgnoreCase))
                        return "Spring Boot";
                    if (content.Contains("quarkus", StringComparison.OrdinalIgnoreCase))
                        return "Quarkus";
                    if (content.Contains("micronaut", StringComparison.OrdinalIgnoreCase))
                        return "Micronaut";
                }
            }
        }
        catch
        {
            // Ignorer les erreurs de lecture
        }

        return null;
    }

    private static (string testFramework, string? testCommand) DetectTestFramework(string projectPath, bool isMaven)
    {
        // JUnit est le standard
        return ("JUnit", isMaven ? "mvn test" : "gradle test");
    }
}