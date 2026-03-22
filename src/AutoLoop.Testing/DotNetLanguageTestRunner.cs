using System.Diagnostics;
using System.Text.RegularExpressions;
using AutoLoop.Core.Models;
using Microsoft.Extensions.Logging;

namespace AutoLoop.Testing;

/// <summary>
/// Runner de tests spécifique aux projets .NET.
/// </summary>
public sealed class DotNetLanguageTestRunner : ILanguageTestRunner
{
    private readonly ILogger<DotNetLanguageTestRunner> _logger;

    public ProjectType[] SupportedProjectTypes => [ProjectType.DotNet];

    public DotNetLanguageTestRunner(ILogger<DotNetLanguageTestRunner> logger)
    {
        _logger = logger;
    }

    public async Task<UnitTestResults> RunTestsAsync(ProjectInfo project, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // Trouver les projets de test
            var testProjects = FindTestProjects(project.ProjectPath);

            if (testProjects.Count == 0)
            {
                _logger.LogWarning("Aucun projet de test trouvé dans {ProjectPath}", project.ProjectPath);
                return new UnitTestResults
                {
                    TotalTests = 0,
                    Passed = 0,
                    Failed = 0,
                    Skipped = 0,
                    Duration = sw.Elapsed,
                    Failures = []
                };
            }

            var allResults = new List<UnitTestResults>();

            foreach (var testProject in testProjects)
            {
                _logger.LogDebug("Exécution des tests pour: {TestProject}", testProject);
                var results = await RunDotnetTestAsync(testProject, ct);
                allResults.Add(results);
            }

            // Agréger les résultats
            var total = allResults.Sum(r => r.TotalTests);
            var passed = allResults.Sum(r => r.Passed);
            var failed = allResults.Sum(r => r.Failed);
            var skipped = allResults.Sum(r => r.Skipped);
            var allFailures = allResults.SelectMany(r => r.Failures).ToList();

            return new UnitTestResults
            {
                TotalTests = total,
                Passed = passed,
                Failed = failed,
                Skipped = skipped,
                Duration = sw.Elapsed,
                Failures = allFailures
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'exécution des tests .NET");
            return new UnitTestResults
            {
                TotalTests = 0,
                Passed = 0,
                Failed = 1,
                Skipped = 0,
                Duration = sw.Elapsed,
                Failures = [new TestFailure
                {
                    TestName = "Test Execution",
                    Message = ex.Message,
                    StackTrace = ex.StackTrace ?? ""
                }]
            };
        }
    }

    private List<string> FindTestProjects(string projectPath)
    {
        var testProjects = new List<string>();

        // Chercher les fichiers .csproj contenant "Test" ou dans un dossier "tests"
        var allCsprojs = Directory.GetFiles(projectPath, "*.csproj", SearchOption.AllDirectories);

        foreach (var csproj in allCsprojs)
        {
            var fileName = Path.GetFileNameWithoutExtension(csproj);
            var directory = Path.GetDirectoryName(csproj) ?? "";

            // Vérifier si c'est un projet de test
            if (fileName.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("Tests", StringComparison.OrdinalIgnoreCase) ||
                directory.Contains("tests", StringComparison.OrdinalIgnoreCase) ||
                directory.Contains("test", StringComparison.OrdinalIgnoreCase))
            {
                testProjects.Add(csproj);
            }
            else
            {
                // Vérifier le contenu pour xUnit, NUnit, MSTest
                try
                {
                    var content = File.ReadAllText(csproj);
                    if (content.Contains("xunit") || content.Contains("NUnit") || content.Contains("Microsoft.NET.Test.Sdk"))
                    {
                        testProjects.Add(csproj);
                    }
                }
                catch
                {
                    // Ignorer les erreurs de lecture
                }
            }
        }

        return testProjects;
    }

    private async Task<UnitTestResults> RunDotnetTestAsync(string testProject, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"test \"{testProject}\" --verbosity normal",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        _logger.LogDebug("Exécution: dotnet test {TestProject}", testProject);

        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        // Parser les résultats
        var passedMatch = Regex.Match(stdout, @"Passed!\s+-\s+(\d+) passed", RegexOptions.IgnoreCase);
        var failedMatch = Regex.Match(stdout, @"Failed\s+-\s+(\d+) failed", RegexOptions.IgnoreCase);
        var skippedMatch = Regex.Match(stdout, @"Skipped\s+-\s+(\d+) skipped", RegexOptions.IgnoreCase);
        var totalMatch = Regex.Match(stdout, @"Total tests:\s*(\d+)", RegexOptions.IgnoreCase);

        var passed = passedMatch.Success && int.TryParse(passedMatch.Groups[1].Value, out var p) ? p : 0;
        var failed = failedMatch.Success && int.TryParse(failedMatch.Groups[1].Value, out var f) ? f : 0;
        var skipped = skippedMatch.Success && int.TryParse(skippedMatch.Groups[1].Value, out var s) ? s : 0;
        var total = totalMatch.Success && int.TryParse(totalMatch.Groups[1].Value, out var t) ? t : passed + failed + skipped;

        // Extraire les échecs
        var failures = new List<TestFailure>();
        var failureMatches = Regex.Matches(stdout, @"Failed\s+(.+?)\s+\[.+?\]", RegexOptions.IgnoreCase);
        foreach (Match match in failureMatches)
        {
            failures.Add(new TestFailure
            {
                TestName = match.Groups[1].Value.Trim(),
                Message = "Test failed",
                StackTrace = ""
            });
        }

        return new UnitTestResults
        {
            TotalTests = total > 0 ? total : passed + failed + skipped,
            Passed = passed,
            Failed = failed,
            Skipped = skipped,
            Duration = TimeSpan.Zero,
            Failures = failures
        };
    }
}