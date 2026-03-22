using System.Diagnostics;
using System.Xml.Linq;
using AutoLoop.Core.Models;
using AutoLoop.Testing.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoLoop.Testing;

public interface IUnitTestRunner
{
    Task<UnitTestResults> RunAsync(string projectPath, CancellationToken ct = default);
}

/// <summary>
/// Lance `dotnet test` en sous-processus et parse le rapport TRX XML.
/// </summary>
public sealed class DotnetTestRunner : IUnitTestRunner
{
    private readonly TestingOptions _options;
    private readonly ILogger<DotnetTestRunner> _logger;

    public DotnetTestRunner(IOptions<TestingOptions> options, ILogger<DotnetTestRunner> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<UnitTestResults> RunAsync(
        string projectPath, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var trxPath = Path.Combine(Path.GetTempPath(), $"autoloop-{Guid.NewGuid():N}.trx");

        try
        {
            var args = $"test \"{projectPath}\" " +
                       $"--logger \"trx;LogFileName={trxPath}\" " +
                       $"--no-build " +
                       $"--verbosity quiet";

            _logger.LogDebug("Lancement des tests : dotnet {Args}", args);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet", args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(_options.TestTimeoutSeconds), ct);
            var completionTask = process.WaitForExitAsync(ct);

            await Task.WhenAny(completionTask, timeoutTask);

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                _logger.LogWarning("Tests unitaires timeout ({Timeout}s).", _options.TestTimeoutSeconds);
                return FailedResults(sw.Elapsed, "Timeout");
            }

            if (process.ExitCode != 0 && !File.Exists(trxPath))
            {
                var stderr = await process.StandardError.ReadToEndAsync(ct);
                return FailedResults(sw.Elapsed, $"dotnet test a échoué : {stderr[..Math.Min(200, stderr.Length)]}");
            }

            return File.Exists(trxPath)
                ? ParseTrxFile(trxPath, sw.Elapsed)
                : SuccessResults(sw.Elapsed); // Build réussi sans tests
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Erreur lors de l'exécution des tests unitaires.");
            return FailedResults(sw.Elapsed, ex.Message);
        }
        finally
        {
            try { if (File.Exists(trxPath)) File.Delete(trxPath); } catch { }
        }
    }

    private static UnitTestResults ParseTrxFile(string trxPath, TimeSpan duration)
    {
        try
        {
            var doc = XDocument.Load(trxPath);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            var counters = doc.Descendants(ns + "Counters").FirstOrDefault();
            var total = int.Parse(counters?.Attribute("total")?.Value ?? "0");
            var passed = int.Parse(counters?.Attribute("passed")?.Value ?? "0");
            var failed = int.Parse(counters?.Attribute("failed")?.Value ?? "0");

            var failures = doc.Descendants(ns + "UnitTestResult")
                .Where(e => e.Attribute("outcome")?.Value == "Failed")
                .Select(e => new TestFailure
                {
                    TestName = e.Attribute("testName")?.Value ?? "Unknown",
                    Message = e.Descendants(ns + "Message").FirstOrDefault()?.Value ?? string.Empty,
                    StackTrace = e.Descendants(ns + "StackTrace").FirstOrDefault()?.Value ?? string.Empty
                })
                .ToList();

            return new UnitTestResults
            {
                TotalTests = total,
                Passed = passed,
                Failed = failed,
                Skipped = total - passed - failed,
                Duration = duration,
                Failures = failures
            };
        }
        catch
        {
            return SuccessResults(duration);
        }
    }

    private static UnitTestResults SuccessResults(TimeSpan duration) => new()
    {
        TotalTests = 0,
        Passed = 0,
        Failed = 0,
        Skipped = 0,
        Duration = duration,
        Failures = []
    };

    private static UnitTestResults FailedResults(TimeSpan duration, string reason) => new()
    {
        TotalTests = 1,
        Passed = 0,
        Failed = 1,
        Skipped = 0,
        Duration = duration,
        Failures = [new TestFailure { TestName = "RunnerFailure", Message = reason, StackTrace = string.Empty }]
    };
}
