using AutoLoop.Core.Models;
using AutoLoop.Testing.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoLoop.Testing;

public interface IRegressionTestRunner
{
    Task<RegressionResults> RunAsync(
        TestSuite? baseline,
        TestSuite candidate,
        CancellationToken ct = default);
}

/// <summary>
/// Compare les métriques du candidat à la baseline.
/// Déclenche un échec si une métrique régresse au-delà du seuil configuré.
/// </summary>
public sealed class BaselineRegressionTestRunner : IRegressionTestRunner
{
    private readonly TestingOptions _options;
    private readonly ILogger<BaselineRegressionTestRunner> _logger;

    public BaselineRegressionTestRunner(
        IOptions<TestingOptions> options,
        ILogger<BaselineRegressionTestRunner> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<RegressionResults> RunAsync(
        TestSuite? baseline,
        TestSuite candidate,
        CancellationToken ct = default)
    {
        var checks = new List<RegressionCheck>();

        if (baseline is null)
        {
            _logger.LogDebug("Aucune baseline disponible — tests de régression ignorés.");
            return Task.FromResult(new RegressionResults
            {
                Checks = [new RegressionCheck
                {
                    CheckName = "BaselineAvailability",
                    Passed = true,
                    FailureReason = null
                }]
            });
        }

        // Vérification 1 : taux de réussite des tests unitaires ne régresse pas
        checks.Add(CheckUnitTestPassRate(baseline, candidate));

        // Vérification 2 : performance moyenne ne régresse pas au-delà du seuil
        checks.Add(CheckPerformanceRegression(baseline, candidate));

        // Vérification 3 : les benchmarks de référence restent dans les limites
        foreach (var baselineBench in baseline.Performance.Benchmarks)
        {
            var candidateBench = candidate.Performance.Benchmarks
                .FirstOrDefault(b => b.BenchmarkName == baselineBench.BenchmarkName);

            if (candidateBench is null) continue;

            checks.Add(CheckBenchmarkRegression(baselineBench, candidateBench));
        }

        return Task.FromResult(new RegressionResults { Checks = checks });
    }

    private RegressionCheck CheckUnitTestPassRate(TestSuite baseline, TestSuite candidate)
    {
        var baselineRate = baseline.UnitTests.PassRate;
        var candidateRate = candidate.UnitTests.PassRate;
        var delta = baselineRate - candidateRate;

        var passed = delta <= 0.01; // Tolérance de 1%
        return new RegressionCheck
        {
            CheckName = "UnitTestPassRate",
            Passed = passed,
            FailureReason = passed
                ? null
                : $"Taux de réussite des tests unitaires : {candidateRate:P1} (baseline : {baselineRate:P1}, delta : -{delta:P1})"
        };
    }

    private RegressionCheck CheckPerformanceRegression(TestSuite baseline, TestSuite candidate)
    {
        if (baseline.Performance.Benchmarks.Count == 0 || candidate.Performance.Benchmarks.Count == 0)
        {
            return new RegressionCheck
            {
                CheckName = "PerformanceRegression",
                Passed = true,
                FailureReason = null
            };
        }

        var baselineScore = baseline.OverallPerformanceScore;
        var candidateScore = candidate.OverallPerformanceScore;

        if (baselineScore <= 0) return new RegressionCheck
        {
            CheckName = "PerformanceRegression",
            Passed = true
        };

        var regressionPct = (baselineScore - candidateScore) / baselineScore * 100;
        var passed = regressionPct <= _options.MaxRegressionPercent;

        return new RegressionCheck
        {
            CheckName = "PerformanceRegression",
            Passed = passed,
            FailureReason = passed
                ? null
                : $"Performance dégradée de {regressionPct:F1}% (seuil : {_options.MaxRegressionPercent}%)"
        };
    }

    private static RegressionCheck CheckBenchmarkRegression(BenchmarkResult baseline, BenchmarkResult candidate)
    {
        // Une latence plus haute = régression
        var deltaPercent = (candidate.MeanNanoseconds - baseline.MeanNanoseconds) / baseline.MeanNanoseconds * 100;
        var passed = deltaPercent <= 10.0; // 10% de tolérance par benchmark

        return new RegressionCheck
        {
            CheckName = $"Benchmark.{baseline.BenchmarkName}",
            Passed = passed,
            FailureReason = passed
                ? null
                : $"'{baseline.BenchmarkName}' : latence +{deltaPercent:F1}% (baseline={baseline.MeanNanoseconds:F0}ns, candidat={candidate.MeanNanoseconds:F0}ns)"
        };
    }
}
