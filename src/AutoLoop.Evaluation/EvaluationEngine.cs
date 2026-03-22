using AutoLoop.Core.Interfaces;
using AutoLoop.Core.Models;
using AutoLoop.Evaluation.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoLoop.Evaluation;

/// <summary>
/// Moteur d'évaluation central.
/// Extrait les échantillons de performance, exécute les tests statistiques,
/// construit le ThresholdComparison, et appelle le DecisionEngine.
/// </summary>
public sealed class EvaluationEngine : IEvaluationEngine
{
    private readonly IStatisticalTestSuite _stats;
    private readonly IDecisionEngine _decisionEngine;
    private readonly EvaluationOptions _options;
    private readonly ILogger<EvaluationEngine> _logger;

    public EvaluationEngine(
        IStatisticalTestSuite stats,
        IDecisionEngine decisionEngine,
        IOptions<EvaluationOptions> options,
        ILogger<EvaluationEngine> logger)
    {
        _stats = stats;
        _decisionEngine = decisionEngine;
        _options = options.Value;
        _logger = logger;
    }

    public Task<EvaluationResult> EvaluateAsync(
        TestSuite? baseline,
        TestSuite candidate,
        CycleContext context,
        CancellationToken ct = default)
    {
        _logger.LogDebug("[Cycle {Id}] Évaluation statistique démarrée.", context.CycleId);

        // Si pas de baseline → on ne peut pas évaluer → DEFER
        if (baseline is null)
        {
            _logger.LogInformation(
                "[Cycle {Id}] Aucune baseline disponible. Décision : DEFER.",
                context.CycleId);

            return Task.FromResult(BuildResult(
                context.CycleId,
                DecisionOutcome.Defer,
                "Aucune baseline disponible — premier cycle.",
                [],
                0.0,
                new ThresholdComparison
                {
                    UnitTestsPassed = candidate.UnitTests.AllPassed,
                    RegressionPassed = true,
                    PerformanceImproved = false,
                    StatisticallySignificant = false,
                    ActualImprovementPercent = 0,
                    RequiredImprovementPercent = _options.MinPerformanceImprovementPercent
                }));
        }

        // Extraire les échantillons bruts des benchmarks
        var baselineSamples = ExtractSamples(baseline);
        var candidateSamples = ExtractSamples(candidate);

        List<StatisticalTestResult> tests;

        if (baselineSamples.Count < 5 || candidateSamples.Count < 5)
        {
            // Pas assez de données pour des tests statistiques fiables
            tests = [];
            _logger.LogWarning(
                "[Cycle {Id}] Données insuffisantes pour tests statistiques " +
                "(baseline={B}, candidat={C}).",
                context.CycleId, baselineSamples.Count, candidateSamples.Count);
        }
        else
        {
            // t-test Welch OU Mann-Whitney selon normalité
            var tTest = _stats.RunWelchTTest(baselineSamples, candidateSamples, _options.StatisticalSignificanceAlpha);
            var mwTest = _stats.RunMannWhitneyU(baselineSamples, candidateSamples, _options.StatisticalSignificanceAlpha);
            var cohenD = _stats.ComputeCohensD(baselineSamples, candidateSamples);
            var bootstrap = _stats.ComputeBootstrapCI(
                baselineSamples, candidateSamples,
                _options.BootstrapIterations);

            tests = [tTest, mwTest, cohenD, bootstrap];

            _logger.LogDebug(
                "[Cycle {Id}] Tests statistiques — t={Tp:F4}, MW={Mp:F4}, d={D:F3}, Boot={Bsig}",
                context.CycleId,
                tTest.PValue, mwTest.PValue,
                cohenD.EffectSize,
                bootstrap.IsSignificant);
        }

        // Calcul du score d'amélioration global
        var actualImprovement = ComputeImprovementScore(baseline, candidate);

        var comparison = new ThresholdComparison
        {
            UnitTestsPassed = candidate.UnitTests.AllPassed,
            RegressionPassed = candidate.Regression.AllPassed,
            PerformanceImproved = candidate.OverallPerformanceScore > baseline.OverallPerformanceScore,
            StatisticallySignificant = tests.Any(t => t.IsSignificant),
            ActualImprovementPercent = actualImprovement,
            RequiredImprovementPercent = _options.MinPerformanceImprovementPercent
        };

        var decision = _decisionEngine.Decide(comparison, tests, _options);
        var rationale = BuildRationale(decision, comparison, tests);

        _logger.LogInformation(
            "[Cycle {Id}] Décision={Decision}, Amélioration={Score:F2}%. {Rationale}",
            context.CycleId, decision, actualImprovement, rationale);

        return Task.FromResult(BuildResult(
            context.CycleId, decision, rationale, tests, actualImprovement, comparison));
    }

    private static List<double> ExtractSamples(TestSuite suite)
        => suite.Performance.Benchmarks
            .SelectMany(b => b.RawSamples)
            .ToList();

    private static double ComputeImprovementScore(TestSuite baseline, TestSuite candidate)
    {
        if (baseline.OverallPerformanceScore <= 0) return 0.0;
        return (candidate.OverallPerformanceScore - baseline.OverallPerformanceScore)
               / baseline.OverallPerformanceScore * 100.0;
    }

    private static string BuildRationale(
        DecisionOutcome decision,
        ThresholdComparison comparison,
        IReadOnlyList<StatisticalTestResult> tests)
    {
        return decision switch
        {
            DecisionOutcome.Accept =>
                $"Amélioration de {comparison.ActualImprovementPercent:F2}% validée statistiquement. " +
                $"Tests unitaires : {(comparison.UnitTestsPassed ? "OK" : "KO")}. " +
                $"Régression : {(comparison.RegressionPassed ? "OK" : "KO")}.",

            DecisionOutcome.Reject when !comparison.UnitTestsPassed =>
                "REJETÉ : tests unitaires échoués.",

            DecisionOutcome.Reject when !comparison.RegressionPassed =>
                "REJETÉ : régression détectée sur les métriques de référence.",

            DecisionOutcome.Reject =>
                $"REJETÉ : amélioration insuffisante ({comparison.ActualImprovementPercent:F2}% " +
                $"< {comparison.RequiredImprovementPercent:F2}% requis) ou non significative.",

            DecisionOutcome.Defer =>
                $"DIFFÉRÉ : données insuffisantes pour conclure. " +
                $"Amélioration observée : {comparison.ActualImprovementPercent:F2}%.",

            _ => "Décision inconnue."
        };
    }

    private static EvaluationResult BuildResult(
        CycleId cycleId,
        DecisionOutcome decision,
        string rationale,
        IReadOnlyList<StatisticalTestResult> tests,
        double improvementScore,
        ThresholdComparison comparison) =>
        new()
        {
            CycleId = cycleId,
            Decision = decision,
            DecisionRationale = rationale,
            StatisticalTests = tests,
            OverallImprovementScore = improvementScore,
            ThresholdComparison = comparison,
            EvaluatedAt = DateTimeOffset.UtcNow
        };
}
