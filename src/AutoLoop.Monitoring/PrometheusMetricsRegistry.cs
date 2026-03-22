using AutoLoop.Core.Interfaces;
using AutoLoop.Core.Models;
using Prometheus;

namespace AutoLoop.Monitoring;

/// <summary>
/// Registre de métriques Prometheus. Expose /metrics via prometheus-net.AspNetCore.
/// </summary>
public sealed class PrometheusMetricsRegistry : IMetricsRegistry
{
    // Counters
    private static readonly Counter CyclesStarted = Metrics
        .CreateCounter("autoloop_cycles_started_total", "Nombre total de cycles démarrés.");

    private static readonly Counter CyclesCompleted = Metrics
        .CreateCounter("autoloop_cycles_completed_total", "Nombre total de cycles terminés.",
            new CounterConfiguration { LabelNames = ["status"] });

    private static readonly Counter GitHubApiCalls = Metrics
        .CreateCounter("autoloop_github_api_calls_total", "Appels à l'API GitHub.",
            new CounterConfiguration { LabelNames = ["operation", "success"] });

    private static readonly Counter Rollbacks = Metrics
        .CreateCounter("autoloop_rollbacks_total", "Rollbacks exécutés.",
            new CounterConfiguration { LabelNames = ["reason"] });

    private static readonly Counter Decisions = Metrics
        .CreateCounter("autoloop_decisions_total", "Décisions d'évaluation.",
            new CounterConfiguration { LabelNames = ["outcome"] });

    // Histograms
    private static readonly Histogram CycleDuration = Metrics
        .CreateHistogram("autoloop_cycle_duration_seconds", "Durée d'un cycle complet.",
            new HistogramConfiguration
            {
                Buckets = Histogram.ExponentialBuckets(1, 2, 10),
                LabelNames = ["status"]
            });

    private static readonly Histogram TestDuration = Metrics
        .CreateHistogram("autoloop_test_duration_seconds", "Durée des suites de tests.",
            new HistogramConfiguration
            {
                Buckets = Histogram.LinearBuckets(0.5, 0.5, 20),
                LabelNames = ["test_type"]
            });

    private static readonly Histogram GitHubApiDuration = Metrics
        .CreateHistogram("autoloop_github_api_duration_seconds", "Durée des appels GitHub.",
            new HistogramConfiguration
            {
                Buckets = Histogram.LinearBuckets(0.1, 0.5, 10),
                LabelNames = ["operation"]
            });

    // Gauges
    private static readonly Gauge HypothesesLastCycle = Metrics
        .CreateGauge("autoloop_hypotheses_last_cycle", "Hypothèses générées au dernier cycle.");

    private static readonly Gauge LastImprovementScore = Metrics
        .CreateGauge("autoloop_last_improvement_score_percent", "Score d'amélioration du dernier cycle accepté.");

    private static readonly Gauge ConsecutiveFailures = Metrics
        .CreateGauge("autoloop_consecutive_failures", "Nombre de cycles consécutifs échoués.");

    private int _consecutiveFailures;

    public void RecordCycleStarted(CycleId cycleId)
        => CyclesStarted.Inc();

    public void RecordCycleCompleted(CycleId cycleId, CycleStatus status, TimeSpan duration)
    {
        var label = status.ToString().ToLowerInvariant();
        CyclesCompleted.WithLabels(label).Inc();
        CycleDuration.WithLabels(label).Observe(duration.TotalSeconds);

        if (status is CycleStatus.Failed or CycleStatus.Rejected or CycleStatus.RolledBack)
        {
            _consecutiveFailures++;
        }
        else
        {
            _consecutiveFailures = 0;
        }

        ConsecutiveFailures.Set(_consecutiveFailures);
    }

    public void RecordTestDuration(string testType, TimeSpan duration)
        => TestDuration.WithLabels(testType.ToLowerInvariant()).Observe(duration.TotalSeconds);

    public void RecordHypothesisGenerated(int count)
        => HypothesesLastCycle.Set(count);

    public void RecordDecision(DecisionOutcome outcome)
        => Decisions.WithLabels(outcome.ToString().ToLowerInvariant()).Inc();

    public void RecordGitHubApiCall(string operation, bool success, TimeSpan duration)
    {
        GitHubApiCalls.WithLabels(operation, success.ToString().ToLower()).Inc();
        GitHubApiDuration.WithLabels(operation).Observe(duration.TotalSeconds);
    }

    public void RecordRollback(RollbackReason reason)
        => Rollbacks.WithLabels(reason.ToString().ToLowerInvariant()).Inc();

    public void RecordClaudeCodeCall(CyclePhase phase, bool success, TimeSpan duration, int inputTokens, int outputTokens)
    {
        // Métriques pour les appels Claude Code
        GitHubApiCalls.WithLabels($"claude_{phase.ToString().ToLowerInvariant()}", success.ToString().ToLower()).Inc();
        GitHubApiDuration.WithLabels($"claude_{phase.ToString().ToLowerInvariant()}").Observe(duration.TotalSeconds);
    }

    public void UpdateImprovementScore(double score)
        => LastImprovementScore.Set(score);
}
