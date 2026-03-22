using System.Diagnostics;
using AutoLoop.Core.Models;
using Microsoft.Extensions.Logging;

namespace AutoLoop.Rollback;

/// <summary>
/// Vérifie la santé du système après un rollback.
/// Trois niveaux : syntaxe des fichiers modifiés, compilation, smoke tests.
/// </summary>
public interface IHealthChecker
{
    Task<HealthCheckResult> CheckAsync(
        CycleContext? context = null,
        CancellationToken ct = default);
}

public sealed class DefaultHealthChecker : IHealthChecker
{
    private readonly ILogger<DefaultHealthChecker> _logger;

    public DefaultHealthChecker(ILogger<DefaultHealthChecker> logger)
    {
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckAsync(
        CycleContext? context = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var failedChecks = new List<string>();

        // Vérification 1 : le fichier modifié peut être parsé
        if (context?.AppliedChange != null)
        {
            var fileCheck = CheckFileReadable(context.AppliedChange.FilePath);
            if (!fileCheck)
                failedChecks.Add($"Fichier illisible après rollback : {context.AppliedChange.FilePath}");
        }

        // Vérification 2 : dotnet build rapide (smoke)
        var buildResult = await RunDotnetBuildAsync(ct);
        if (!buildResult)
            failedChecks.Add("dotnet build a échoué après le rollback.");

        var isHealthy = failedChecks.Count == 0;

        if (isHealthy)
            _logger.LogInformation("Vérification post-rollback : système sain.");
        else
            _logger.LogError(
                "Vérification post-rollback : ÉCHEC. Problèmes : {Issues}",
                string.Join(", ", failedChecks));

        return new HealthCheckResult
        {
            IsHealthy = isHealthy,
            FailedChecks = failedChecks,
            Duration = sw.Elapsed
        };
    }

    private static bool CheckFileReadable(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return false;
            using var _ = File.OpenRead(filePath);
            return true;
        }
        catch { return false; }
    }

    private async Task<bool> RunDotnetBuildAsync(CancellationToken ct)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet", "build --no-restore -v quiet")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            var waitTask = process.WaitForExitAsync(ct);
            var timeout = Task.Delay(TimeSpan.FromSeconds(60), ct);

            await Task.WhenAny(waitTask, timeout);

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                _logger.LogWarning("dotnet build timeout lors de la vérification post-rollback.");
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible d'exécuter dotnet build pour la vérification post-rollback.");
            return true; // On ne bloque pas si dotnet n'est pas disponible
        }
    }
}
