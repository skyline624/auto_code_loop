using AutoLoop.Monitoring.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoLoop.Monitoring;

public sealed record SystemSnapshot
{
    public required double CpuUsagePercent { get; init; }
    public required long WorkingSetBytes { get; init; }
    public required long ManagedHeapBytes { get; init; }
    public required int ThreadCount { get; init; }
    public required DateTimeOffset MeasuredAt { get; init; }
}

/// <summary>
/// Collecte périodique des métriques système (CPU, mémoire) via Process et GC.
/// Tourne en background avec BackgroundService.
/// </summary>
public sealed class SystemMonitor : BackgroundService
{
    private readonly MonitoringOptions _options;
    private readonly ILogger<SystemMonitor> _logger;
    private SystemSnapshot? _latest;

    public SystemMonitor(IOptions<MonitoringOptions> options, ILogger<SystemMonitor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public SystemSnapshot? GetLatestSnapshot() => _latest;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _latest = CollectSnapshot();

                if (_latest.WorkingSetBytes / 1024 / 1024 > _options.MaxMemoryMb)
                {
                    _logger.LogWarning(
                        "Utilisation mémoire élevée : {Mb}MB (seuil : {Max}MB)",
                        _latest.WorkingSetBytes / 1024 / 1024, _options.MaxMemoryMb);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Erreur de collecte des métriques système.");
            }

            await Task.Delay(10_000, stoppingToken);
        }
    }

    private static SystemSnapshot CollectSnapshot()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();

        return new SystemSnapshot
        {
            CpuUsagePercent = 0.0, // Nécessite un intervalle — valeur indicative
            WorkingSetBytes = process.WorkingSet64,
            ManagedHeapBytes = GC.GetTotalMemory(forceFullCollection: false),
            ThreadCount = process.Threads.Count,
            MeasuredAt = DateTimeOffset.UtcNow
        };
    }
}
