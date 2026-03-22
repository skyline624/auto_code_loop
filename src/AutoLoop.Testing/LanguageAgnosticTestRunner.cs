using System.Diagnostics;
using System.Text.RegularExpressions;
using AutoLoop.Core.Interfaces;
using AutoLoop.Core.Models;
using AutoLoop.Testing.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoLoop.Testing;

/// <summary>
/// Interface pour les runners de tests spécifiques aux langages.
/// </summary>
public interface ILanguageTestRunner
{
    /// <summary>Types de projets supportés par ce runner.</summary>
    ProjectType[] SupportedProjectTypes { get; }

    /// <summary>Exécute les tests pour le projet donné.</summary>
    Task<UnitTestResults> RunTestsAsync(ProjectInfo project, CancellationToken ct = default);
}

/// <summary>
/// Runner de tests multi-langage qui détecte automatiquement le framework de test
/// et exécute les commandes appropriées.
/// </summary>
public sealed class LanguageAgnosticTestRunner : ITestRunner
{
    private readonly ILogger<LanguageAgnosticTestRunner> _logger;
    private readonly IMetricsRegistry _metrics;
    private readonly TestingOptions _options;
    private readonly IEnumerable<ILanguageTestRunner> _languageRunners;
    private readonly IBaselineStore _baselineStore;
    private readonly IRegressionTestRunner _regressionRunner;

    // Commandes de test par type de projet
    private static readonly Dictionary<ProjectType, TestCommandInfo> TestCommands = new()
    {
        [ProjectType.NodeJs] = new("npm", "test", "jest|vitest|mocha|jasmine"),
        [ProjectType.Python] = new("pytest", ".", "pytest|unittest"),
        [ProjectType.DotNet] = new("dotnet", "test", "xunit|nunit|mstest"),
        [ProjectType.Rust] = new("cargo", "test", "built-in"),
        [ProjectType.Go] = new("go", "test ./...", "built-in"),
        [ProjectType.Java] = new("mvn", "test", "junit|testng"),
        [ProjectType.Ruby] = new("bundle", "exec rspec", "rspec"),
        [ProjectType.Php] = new("phpunit", "", "phpunit")
    };

    public LanguageAgnosticTestRunner(
        IEnumerable<ILanguageTestRunner> languageRunners,
        IBaselineStore baselineStore,
        IRegressionTestRunner regressionRunner,
        IMetricsRegistry metrics,
        IOptions<TestingOptions> options,
        ILogger<LanguageAgnosticTestRunner> logger)
    {
        _languageRunners = languageRunners;
        _baselineStore = baselineStore;
        _regressionRunner = regressionRunner;
        _metrics = metrics;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TestSuite> RunAllTestsAsync(CycleContext context, CancellationToken ct = default)
    {
        if (context.Project == null)
        {
            throw new InvalidOperationException("Aucun projet détecté. Impossible d'exécuter les tests.");
        }

        var startedAt = DateTimeOffset.UtcNow;
        _logger.LogInformation("[Cycle {Id}] Démarrage des tests pour {ProjectType}...",
            context.CycleId, context.Project.Type);

        // 1. Exécuter les tests unitaires
        var unitSw = Stopwatch.StartNew();
        var unitResults = await RunUnitTestsAsync(context.Project, ct);
        _metrics.RecordTestDuration("unit", unitSw.Elapsed);

        _logger.LogInformation("[Cycle {Id}] Tests unitaires: {Passed}/{Total} passés ({Failed} échoués)",
            context.CycleId, unitResults.Passed, unitResults.TotalTests, unitResults.Failed);

        // 2. Récupérer la baseline pour les tests de régression
        var baseline = await _baselineStore.GetLatestBaselineAsync(ct);

        // 3. Exécuter les tests de régression
        var regSw = Stopwatch.StartNew();
        var regResults = await RunRegressionTestsAsync(context, baseline, unitResults, ct);
        _metrics.RecordTestDuration("regression", regSw.Elapsed);

        // 4. Construire le TestSuite
        var suite = new TestSuite
        {
            CycleId = context.CycleId,
            RecordedAt = DateTimeOffset.UtcNow,
            UnitTests = unitResults,
            Performance = new PerformanceResults { Benchmarks = [] },
            Regression = regResults
        };

        _logger.LogInformation(
            "[Cycle {Id}] Tests terminés. AllPassed={Passed} | Unit={U}/{Total} | Regression={Reg}",
            context.CycleId,
            suite.AllPassed,
            unitResults.Passed,
            unitResults.TotalTests,
            regResults.AllPassed);

        return suite;
    }

    private async Task<UnitTestResults> RunUnitTestsAsync(ProjectInfo project, CancellationToken ct)
    {
        // Essayer d'abord avec les runners spécifiques
        foreach (var runner in _languageRunners.Where(r => r.SupportedProjectTypes.Contains(project.Type)))
        {
            _logger.LogDebug("Utilisation du runner spécifique pour {ProjectType}", project.Type);
            return await runner.RunTestsAsync(project, ct);
        }

        // Fallback: utiliser les commandes par défaut
        if (!TestCommands.TryGetValue(project.Type, out var commandInfo))
        {
            _logger.LogWarning("Aucune commande de test connue pour {ProjectType}. Tentative de détection automatique.", project.Type);
            return await DetectAndRunTestsAsync(project, ct);
        }

        return await ExecuteTestCommandAsync(project, commandInfo, ct);
    }

    private async Task<UnitTestResults> ExecuteTestCommandAsync(
        ProjectInfo project,
        TestCommandInfo commandInfo,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // Construire la commande
            var executable = commandInfo.Executable;
            var args = commandInfo.Arguments;

            // Utiliser la commande personnalisée du projet si définie
            if (!string.IsNullOrEmpty(project.TestCommand))
            {
                var parts = project.TestCommand.Split(' ', 2);
                executable = parts[0];
                args = parts.Length > 1 ? parts[1] : "";
            }

            _logger.LogInformation("Exécution: {Executable} {Args}", executable, args);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = args,
                    WorkingDirectory = project.ProjectPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_options.TestTimeoutMs);

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill();
                _logger.LogWarning("Tests annulés après timeout de {Timeout}ms", _options.TestTimeoutMs);
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

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            // Parser les résultats
            var results = ParseTestResults(stdout, stderr, project.Type, commandInfo.FrameworkPattern);
            results = results with { Duration = sw.Elapsed };

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'exécution des tests pour {ProjectType}", project.Type);
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

    private async Task<UnitTestResults> DetectAndRunTestsAsync(ProjectInfo project, CancellationToken ct)
    {
        // Essayer de détecter un framework de test
        var detectedCommand = await TryDetectTestCommandAsync(project);

        if (detectedCommand == null)
        {
            _logger.LogError("Impossible de détecter un framework de test pour {ProjectType}", project.Type);
            return new UnitTestResults
            {
                TotalTests = 0,
                Passed = 0,
                Failed = 0,
                Skipped = 0,
                Duration = TimeSpan.Zero,
                Failures = []
            };
        }

        return await ExecuteTestCommandAsync(project, detectedCommand, ct);
    }

    private async Task<TestCommandInfo?> TryDetectTestCommandAsync(ProjectInfo project)
    {
        // Vérifier les fichiers de configuration courants
        var testConfigFiles = new[]
        {
            "jest.config.js", "jest.config.ts", "vitest.config.ts", "pytest.ini", "setup.cfg",
            "phpunit.xml", "phpunit.xml.dist"
        };

        foreach (var file in testConfigFiles)
        {
            var filePath = Path.Combine(project.ProjectPath, file);
            if (File.Exists(filePath))
            {
                _logger.LogDebug("Fichier de config de test détecté: {File}", file);

                return file switch
                {
                    var f when f.StartsWith("jest") => new TestCommandInfo("npm", "test", "jest"),
                    var f when f.StartsWith("vitest") => new TestCommandInfo("npm", "test", "vitest"),
                    var f when f.StartsWith("pytest") => new TestCommandInfo("pytest", ".", "pytest"),
                    var f when f.StartsWith("phpunit") => new TestCommandInfo("phpunit", "", "phpunit"),
                    _ => null
                };
            }
        }

        // Vérifier package.json pour les projets Node.js
        if (project.Type == ProjectType.NodeJs)
        {
            var packageJsonPath = Path.Combine(project.ProjectPath, "package.json");
            if (File.Exists(packageJsonPath))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(packageJsonPath);
                    if (content.Contains("\"test\""))
                    {
                        return new TestCommandInfo("npm", "test", "npm-test");
                    }
                }
                catch
                {
                    // Ignorer les erreurs de lecture
                }
            }
        }

        return null;
    }

    private UnitTestResults ParseTestResults(
        string stdout,
        string stderr,
        ProjectType projectType,
        string frameworkPattern)
    {
        // Parser selon le framework de test
        return projectType switch
        {
            ProjectType.DotNet => ParseDotNetTestResults(stdout, stderr),
            ProjectType.Python => ParsePytestResults(stdout, stderr),
            ProjectType.NodeJs => ParseNodeTestResults(stdout, stderr, frameworkPattern),
            ProjectType.Rust => ParseCargoTestResults(stdout, stderr),
            ProjectType.Go => ParseGoTestResults(stdout, stderr),
            ProjectType.Java => ParseMvnTestResults(stdout, stderr),
            _ => ParseGenericResults(stdout, stderr)
        };
    }

    private UnitTestResults ParseDotNetTestResults(string stdout, string stderr)
    {
        // Parser le format TRX ou la sortie console de dotnet test
        var passedMatch = Regex.Match(stdout, @"Passed!\s+-\s+(\d+) passed", RegexOptions.IgnoreCase);
        var failedMatch = Regex.Match(stdout, @"Failed\s+-\s+(\d+) failed", RegexOptions.IgnoreCase);
        var skippedMatch = Regex.Match(stdout, @"Skipped\s+-\s+(\d+) skipped", RegexOptions.IgnoreCase);

        var passed = passedMatch.Success && int.TryParse(passedMatch.Groups[1].Value, out var p) ? p : 0;
        var failed = failedMatch.Success && int.TryParse(failedMatch.Groups[1].Value, out var f) ? f : 0;
        var skipped = skippedMatch.Success && int.TryParse(skippedMatch.Groups[1].Value, out var s) ? s : 0;

        // Chercher aussi le format "Total tests: X"
        var totalMatch = Regex.Match(stdout, @"Total tests:\s*(\d+)", RegexOptions.IgnoreCase);
        var total = totalMatch.Success && int.TryParse(totalMatch.Groups[1].Value, out var t) ? t : passed + failed + skipped;

        var failures = ExtractFailures(stdout, @"Failed\s+(.+?)\s+\[.+?\]", projectType: ProjectType.DotNet);

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

    private UnitTestResults ParsePytestResults(string stdout, string stderr)
    {
        // Parser la sortie de pytest
        // Format: "X passed, Y failed, Z skipped" ou "X passed in Y.Zs"
        var passedMatch = Regex.Match(stdout, @"(\d+)\s+passed", RegexOptions.IgnoreCase);
        var failedMatch = Regex.Match(stdout, @"(\d+)\s+failed", RegexOptions.IgnoreCase);
        var skippedMatch = Regex.Match(stdout, @"(\d+)\s+skipped", RegexOptions.IgnoreCase);
        var errorMatch = Regex.Match(stdout, @"(\d+)\s+error", RegexOptions.IgnoreCase);

        var passed = passedMatch.Success && int.TryParse(passedMatch.Groups[1].Value, out var p) ? p : 0;
        var failed = failedMatch.Success && int.TryParse(failedMatch.Groups[1].Value, out var f) ? f : 0;
        var errors = errorMatch.Success && int.TryParse(errorMatch.Groups[1].Value, out var e) ? e : 0;
        var skipped = skippedMatch.Success && int.TryParse(skippedMatch.Groups[1].Value, out var s) ? s : 0;

        var failures = ExtractFailures(stdout, @"FAILED\s+(.+?)(?:\s|$)", projectType: ProjectType.Python);

        return new UnitTestResults
        {
            TotalTests = passed + failed + errors + skipped,
            Passed = passed,
            Failed = failed + errors,
            Skipped = skipped,
            Duration = TimeSpan.Zero,
            Failures = failures
        };
    }

    private UnitTestResults ParseNodeTestResults(string stdout, string stderr, string frameworkPattern)
    {
        // Parser la sortie de Jest/Vitest/Mocha
        // Format Jest: "Tests: X passed, Y failed, Z total"
        var passedMatch = Regex.Match(stdout, @"(\d+)\s+passed", RegexOptions.IgnoreCase);
        var failedMatch = Regex.Match(stdout, @"(\d+)\s+failed", RegexOptions.IgnoreCase);
        var skippedMatch = Regex.Match(stdout, @"(\d+)\s+skipped", RegexOptions.IgnoreCase);
        var totalMatch = Regex.Match(stdout, @"Tests?:\s*(\d+)", RegexOptions.IgnoreCase);

        var passed = passedMatch.Success && int.TryParse(passedMatch.Groups[1].Value, out var p) ? p : 0;
        var failed = failedMatch.Success && int.TryParse(failedMatch.Groups[1].Value, out var f) ? f : 0;
        var skipped = skippedMatch.Success && int.TryParse(skippedMatch.Groups[1].Value, out var s) ? s : 0;
        var total = totalMatch.Success && int.TryParse(totalMatch.Groups[1].Value, out var t) ? t : passed + failed + skipped;

        var failures = ExtractFailures(stdout, @"FAIL\s+(.+?)(?:\n|$)", projectType: ProjectType.NodeJs);

        return new UnitTestResults
        {
            TotalTests = total,
            Passed = passed,
            Failed = failed,
            Skipped = skipped,
            Duration = TimeSpan.Zero,
            Failures = failures
        };
    }

    private UnitTestResults ParseCargoTestResults(string stdout, string stderr)
    {
        // Parser la sortie de cargo test
        // Format: "test result: X passed; Y failed; Z ignored"
        var resultMatch = Regex.Match(stdout, @"test result:\s*(\d+)\s+passed;?\s*(\d+)\s+failed;?\s*(\d+)\s+ignored", RegexOptions.IgnoreCase);
        var simpleMatch = Regex.Match(stdout, @"test result:\s*ok\.?\s*(\d+)\s+passed", RegexOptions.IgnoreCase);

        int passed, failed, skipped;
        if (resultMatch.Success)
        {
            passed = int.TryParse(resultMatch.Groups[1].Value, out var p) ? p : 0;
            failed = int.TryParse(resultMatch.Groups[2].Value, out var f) ? f : 0;
            skipped = int.TryParse(resultMatch.Groups[3].Value, out var s) ? s : 0;
        }
        else if (simpleMatch.Success)
        {
            passed = int.TryParse(simpleMatch.Groups[1].Value, out var p) ? p : 0;
            failed = 0;
            skipped = 0;
        }
        else
        {
            passed = 0;
            failed = 0;
            skipped = 0;
        }

        var failures = ExtractFailures(stdout, @"test\s+(.+?)\s+...+\s+FAILED", projectType: ProjectType.Rust);

        return new UnitTestResults
        {
            TotalTests = passed + failed + skipped,
            Passed = passed,
            Failed = failed,
            Skipped = skipped,
            Duration = TimeSpan.Zero,
            Failures = failures
        };
    }

    private UnitTestResults ParseGoTestResults(string stdout, string stderr)
    {
        // Parser la sortie de go test
        // Format: "PASS" ou "FAIL" avec "ok package X.XXXs"
        var okMatch = Regex.Match(stdout, @"ok\s+\S+\s+([\d.]+)s", RegexOptions.IgnoreCase);
        var failMatch = Regex.Match(stdout, @"FAIL\s+\S+\s+([\d.]+)s", RegexOptions.IgnoreCase);

        var lines = stdout.Split('\n');
        var passed = lines.Count(l => l.Contains("PASS") || l.Trim().StartsWith("ok"));
        var failed = lines.Count(l => l.Contains("FAIL") && !l.Contains("PASS"));

        var failures = ExtractFailures(stdout, @"---\s+FAIL:\s+(.+?)(?:\s|$)", projectType: ProjectType.Go);

        return new UnitTestResults
        {
            TotalTests = passed + failed,
            Passed = passed,
            Failed = failed,
            Skipped = 0,
            Duration = TimeSpan.Zero,
            Failures = failures
        };
    }

    private UnitTestResults ParseMvnTestResults(string stdout, string stderr)
    {
        // Parser la sortie de mvn test
        // Format: "Tests run: X, Failures: Y, Errors: Z, Skipped: W"
        var runMatch = Regex.Match(stdout, @"Tests run:\s*(\d+)", RegexOptions.IgnoreCase);
        var failMatch = Regex.Match(stdout, @"Failures:\s*(\d+)", RegexOptions.IgnoreCase);
        var errMatch = Regex.Match(stdout, @"Errors:\s*(\d+)", RegexOptions.IgnoreCase);
        var skipMatch = Regex.Match(stdout, @"Skipped:\s*(\d+)", RegexOptions.IgnoreCase);

        var total = runMatch.Success && int.TryParse(runMatch.Groups[1].Value, out var t) ? t : 0;
        var failed = failMatch.Success && int.TryParse(failMatch.Groups[1].Value, out var f) ? f : 0;
        failed += errMatch.Success && int.TryParse(errMatch.Groups[1].Value, out var e) ? e : 0;
        var skipped = skipMatch.Success && int.TryParse(skipMatch.Groups[1].Value, out var s) ? s : 0;
        var passed = total - failed - skipped;

        var failures = ExtractFailures(stdout, @"Failed\s+tests?:\s*(.+?)(?:\s|$)", projectType: ProjectType.Java);

        return new UnitTestResults
        {
            TotalTests = total,
            Passed = Math.Max(0, passed),
            Failed = failed,
            Skipped = skipped,
            Duration = TimeSpan.Zero,
            Failures = failures
        };
    }

    private UnitTestResults ParseGenericResults(string stdout, string stderr)
    {
        // Parser générique: chercher des patterns courants
        var passedMatch = Regex.Match(stdout, @"(\d+)\s+passed", RegexOptions.IgnoreCase);
        var failedMatch = Regex.Match(stdout, @"(\d+)\s+failed", RegexOptions.IgnoreCase);

        var passed = passedMatch.Success && int.TryParse(passedMatch.Groups[1].Value, out var p) ? p : 0;
        var failed = failedMatch.Success && int.TryParse(failedMatch.Groups[1].Value, out var f) ? f : 0;

        return new UnitTestResults
        {
            TotalTests = passed + failed,
            Passed = passed,
            Failed = failed,
            Skipped = 0,
            Duration = TimeSpan.Zero,
            Failures = []
        };
    }

    private List<TestFailure> ExtractFailures(string output, string pattern, ProjectType projectType)
    {
        var failures = new List<TestFailure>();
        var matches = Regex.Matches(output, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);

        foreach (Match match in matches)
        {
            failures.Add(new TestFailure
            {
                TestName = match.Groups[1].Value.Trim(),
                Message = $"Test failed ({projectType})",
                StackTrace = ""
            });
        }

        return failures;
    }

    private async Task<RegressionResults> RunRegressionTestsAsync(
        CycleContext context,
        TestSuite? baseline,
        UnitTestResults unitResults,
        CancellationToken ct)
    {
        // Utiliser le runner de régression existant
        var tempSuite = new TestSuite
        {
            CycleId = context.CycleId,
            RecordedAt = DateTimeOffset.UtcNow,
            UnitTests = unitResults,
            Performance = new PerformanceResults { Benchmarks = [] },
            Regression = new RegressionResults { Checks = [] }
        };

        return await _regressionRunner.RunAsync(baseline, tempSuite, ct);
    }

    private sealed record TestCommandInfo(string Executable, string Arguments, string FrameworkPattern);
}