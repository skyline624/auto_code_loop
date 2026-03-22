using AutoLoop.Core.Interfaces;
using AutoLoop.Core.Models;
using AutoLoop.Hypothesis.Options;
using AutoLoop.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoLoop.Hypothesis;

/// <summary>
/// Analyse les métriques de performance stockées en baseline pour détecter les dégradations.
/// </summary>
public interface IMetricsAnalyzer
{
    Task<IReadOnlyList<PerformanceMetricSnapshot>> GetDegradedMetricsAsync(
        CancellationToken ct = default);
}

/// <summary>
/// Détecte les patterns d'erreurs récurrents dans les journaux de cycles passés.
/// </summary>
public interface IErrorPatternAnalyzer
{
    Task<IReadOnlyList<ErrorPattern>> GetRecurringErrorsAsync(
        int lookbackCycles,
        CancellationToken ct = default);
}

/// <summary>
/// Analyse l'historique des cycles pour identifier les patterns de succès/échec.
/// </summary>
public interface IHistoryAnalyzer
{
    Task<IReadOnlyList<ChangeRecord>> GetRejectedChangesAsync(
        int lookbackCycles,
        CancellationToken ct = default);

    Task<IReadOnlyList<ChangeRecord>> GetSuccessfulChangesAsync(
        int lookbackCycles,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, double>> GetHypothesisSuccessRatesAsync(
        CancellationToken ct = default);
}

// ── Implémentations ───────────────────────────────────────────────────────────

public sealed class BaselineMetricsAnalyzer : IMetricsAnalyzer
{
    private readonly IBaselineStore _baselineStore;
    private readonly HypothesisOptions _options;

    public BaselineMetricsAnalyzer(IBaselineStore baselineStore, IOptions<HypothesisOptions> options)
    {
        _baselineStore = baselineStore;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<PerformanceMetricSnapshot>> GetDegradedMetricsAsync(
        CancellationToken ct = default)
    {
        var baseline = await _baselineStore.GetLatestBaselineAsync(ct);
        if (baseline is null) return [];

        var degraded = new List<PerformanceMetricSnapshot>();

        // Détecter la dégradation de performance des benchmarks
        foreach (var bench in baseline.Performance.Benchmarks)
        {
            if (bench.NormalizedScore < 0.95) // En dessous de 95% du score maximal
            {
                degraded.Add(new PerformanceMetricSnapshot
                {
                    MetricName = bench.BenchmarkName,
                    Value = bench.MeanNanoseconds,
                    Baseline = bench.MeanNanoseconds * 0.95, // Seuil attendu
                    DeltaPercent = (1.0 - bench.NormalizedScore) * 100,
                    MeasuredAt = baseline.RecordedAt
                });
            }
        }

        return degraded;
    }
}

public sealed class JournalErrorPatternAnalyzer : IErrorPatternAnalyzer
{
    private readonly ICycleJournal _journal;
    private readonly ILogger<JournalErrorPatternAnalyzer> _logger;

    public JournalErrorPatternAnalyzer(
        ICycleJournal journal,
        ILogger<JournalErrorPatternAnalyzer> logger)
    {
        _journal = journal;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ErrorPattern>> GetRecurringErrorsAsync(
        int lookbackCycles,
        CancellationToken ct = default)
    {
        var recentCycles = await _journal.GetRecentCyclesAsync(lookbackCycles, ct);
        var errorCycles = recentCycles
            .Cast<System.Text.Json.JsonElement?>()
            .Count(c => c?.TryGetProperty("Status", out var s) == true &&
                        s.GetString() is "Failed" or "Rejected");

        if (errorCycles == 0) return [];

        // Synthèse simplifiée — en production, on analyserait les stack traces
        return errorCycles > 2
            ? [new ErrorPattern
                {
                    ExceptionType = "RecurringFailure",
                    StackTraceSignature = "multiple_cycle_failures",
                    OccurrenceCount = errorCycles,
                    SourceFile = "unknown",
                    SourceLine = 0
                }]
            : [];
    }
}

public sealed class JournalHistoryAnalyzer : IHistoryAnalyzer
{
    private readonly ICycleJournal _journal;
    private readonly ILogger<JournalHistoryAnalyzer> _logger;

    public JournalHistoryAnalyzer(
        ICycleJournal journal,
        ILogger<JournalHistoryAnalyzer> logger)
    {
        _journal = journal;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ChangeRecord>> GetRejectedChangesAsync(
        int lookbackCycles,
        CancellationToken ct = default)
    {
        // Simplification : on retourne une liste vide — en production on
        // reconstruit les ChangeRecords depuis le storage des changements
        var recentCycles = await _journal.GetRecentCyclesAsync(lookbackCycles, ct);
        return [];
    }

    public async Task<IReadOnlyList<ChangeRecord>> GetSuccessfulChangesAsync(
        int lookbackCycles,
        CancellationToken ct = default)
    {
        var recentCycles = await _journal.GetRecentCyclesAsync(lookbackCycles, ct);
        return [];
    }

    public async Task<IReadOnlyDictionary<string, double>> GetHypothesisSuccessRatesAsync(
        CancellationToken ct = default)
    {
        var recentCycles = await _journal.GetRecentCyclesAsync(50, ct);
        return new Dictionary<string, double>(); // À enrichir en production
    }
}
