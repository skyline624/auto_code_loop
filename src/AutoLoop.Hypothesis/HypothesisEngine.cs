using AutoLoop.Core.Interfaces;
using AutoLoop.Core.Models;
using AutoLoop.Hypothesis.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HypothesisModel = AutoLoop.Core.Models.Hypothesis;

namespace AutoLoop.Hypothesis;

/// <summary>
/// Moteur de génération d'hypothèses.
/// Agrège les signaux (métriques dégradées, erreurs récurrentes, historique),
/// génère des hypothèses, les filtre par confiance et les classe par priorité.
/// </summary>
public sealed class HypothesisEngine : IHypothesisEngine
{
    private readonly IMetricsAnalyzer _metricsAnalyzer;
    private readonly IErrorPatternAnalyzer _errorAnalyzer;
    private readonly IHistoryAnalyzer _historyAnalyzer;
    private readonly IHypothesisRanker _ranker;
    private readonly HypothesisOptions _options;
    private readonly ILogger<HypothesisEngine> _logger;

    public HypothesisEngine(
        IMetricsAnalyzer metricsAnalyzer,
        IErrorPatternAnalyzer errorAnalyzer,
        IHistoryAnalyzer historyAnalyzer,
        IHypothesisRanker ranker,
        IOptions<HypothesisOptions> options,
        ILogger<HypothesisEngine> logger)
    {
        _metricsAnalyzer = metricsAnalyzer;
        _errorAnalyzer = errorAnalyzer;
        _historyAnalyzer = historyAnalyzer;
        _ranker = ranker;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<HypothesisModel>> GenerateHypothesesAsync(
        CycleContext context,
        CancellationToken ct = default)
    {
        // Collecte des signaux en parallèle
        var (metrics, errors, rejectedHistory) = await GatherSignalsAsync(context, ct);

        var raw = new List<HypothesisModel>();
        raw.AddRange(GenerateFromMetrics(metrics, context));
        raw.AddRange(GenerateFromErrors(errors, context));
        raw.AddRange(GenerateFromHistory(rejectedHistory, context));

        // Filtre par seuil de confiance minimal
        var filtered = raw
            .Where(h => h.ConfidenceScore >= _options.MinConfidenceThreshold)
            .ToList();

        // Classement et limitation
        var ranked = _ranker.Rank(filtered)
            .Take(_options.MaxHypotheses)
            .ToList();

        _logger.LogDebug(
            "Hypothèses : {Raw} générées → {Filtered} après filtre → {Final} après classement.",
            raw.Count, filtered.Count, ranked.Count);

        return ranked;
    }

    private async Task<(
        IReadOnlyList<PerformanceMetricSnapshot> metrics,
        IReadOnlyList<ErrorPattern> errors,
        IReadOnlyList<ChangeRecord> history)>
    GatherSignalsAsync(CycleContext context, CancellationToken ct)
    {
        var metricsTask = _metricsAnalyzer.GetDegradedMetricsAsync(ct);
        var errorsTask = _errorAnalyzer.GetRecurringErrorsAsync(_options.LookbackCycles, ct);
        var historyTask = _historyAnalyzer.GetRejectedChangesAsync(_options.LookbackCycles, ct);

        await Task.WhenAll(metricsTask, errorsTask, historyTask);

        return (metricsTask.Result, errorsTask.Result, historyTask.Result);
    }

    private IEnumerable<HypothesisModel> GenerateFromMetrics(
        IReadOnlyList<PerformanceMetricSnapshot> metrics,
        CycleContext context)
    {
        foreach (var metric in metrics)
        {
            if (Math.Abs(metric.DeltaPercent) < _options.MinDeltaPercentToFlag) continue;

            var type = metric.MetricName.Contains("memory", StringComparison.OrdinalIgnoreCase)
                ? HypothesisType.MemoryLeak
                : HypothesisType.PerformanceBottleneck;

            yield return new HypothesisModel
            {
                Id = Guid.NewGuid().ToString("N"),
                CycleId = context.CycleId,
                Type = type,
                TargetFile = "to-be-determined-by-profiling",
                Rationale = $"Métrique '{metric.MetricName}' dégradée de {metric.DeltaPercent:F1}% " +
                            $"(valeur={metric.Value:F2}, baseline={metric.Baseline:F2})",
                Priority = Math.Min(metric.DeltaPercent / 100.0, 1.0),
                ExpectedImpact = Math.Min(metric.DeltaPercent / 50.0, 1.0),
                ConfidenceScore = 0.6, // Confiance modérée sur métriques
                Evidence = new Dictionary<string, object>
                {
                    ["metric_name"] = metric.MetricName,
                    ["delta_percent"] = metric.DeltaPercent,
                    ["current_value"] = metric.Value,
                    ["baseline_value"] = metric.Baseline
                },
                GeneratedAt = DateTimeOffset.UtcNow
            };
        }
    }

    private IEnumerable<HypothesisModel> GenerateFromErrors(
        IReadOnlyList<ErrorPattern> errors,
        CycleContext context)
    {
        foreach (var error in errors.Where(e => e.OccurrenceCount >= 2))
        {
            yield return new HypothesisModel
            {
                Id = Guid.NewGuid().ToString("N"),
                CycleId = context.CycleId,
                Type = HypothesisType.RecurringError,
                TargetFile = error.SourceFile,
                TargetMethod = null,
                Rationale = $"Exception '{error.ExceptionType}' observée {error.OccurrenceCount} fois " +
                            $"(signature={error.StackTraceSignature})",
                Priority = Math.Min(error.OccurrenceCount / 10.0, 1.0),
                ExpectedImpact = 0.7,
                ConfidenceScore = Math.Min(error.OccurrenceCount / 5.0, 1.0),
                Evidence = new Dictionary<string, object>
                {
                    ["exception_type"] = error.ExceptionType,
                    ["occurrence_count"] = error.OccurrenceCount,
                    ["source_line"] = error.SourceLine
                },
                GeneratedAt = DateTimeOffset.UtcNow
            };
        }
    }

    private IEnumerable<HypothesisModel> GenerateFromHistory(
        IReadOnlyList<ChangeRecord> rejected,
        CycleContext context)
    {
        // Si trop de rejets sur un même fichier → explorer un autre algorithme
        var byFile = rejected
            .GroupBy(r => r.FilePath)
            .Where(g => g.Count() >= 2);

        foreach (var group in byFile)
        {
            yield return new HypothesisModel
            {
                Id = Guid.NewGuid().ToString("N"),
                CycleId = context.CycleId,
                Type = HypothesisType.UnoptimizedAlgorithm,
                TargetFile = group.Key,
                Rationale = $"Fichier '{group.Key}' a {group.Count()} rejets consécutifs. " +
                            "Essai d'une stratégie d'algorithme différente.",
                Priority = 0.4,
                ExpectedImpact = 0.5,
                ConfidenceScore = 0.4,
                Evidence = new Dictionary<string, object>
                {
                    ["rejected_count"] = group.Count(),
                    ["target_file"] = group.Key
                },
                GeneratedAt = DateTimeOffset.UtcNow
            };
        }
    }
}
