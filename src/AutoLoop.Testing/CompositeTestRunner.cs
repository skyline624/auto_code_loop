using System.Diagnostics;
using AutoLoop.Core.Interfaces;
using AutoLoop.Core.Models;
using AutoLoop.Testing.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoLoop.Testing;

/// <summary>
/// Orchestre les trois runners de tests : unitaires, performance, régression.
/// Compose le TestSuite complet qui alimente la Phase 4.
/// </summary>
public sealed class CompositeTestRunner : ITestRunner
{
    private readonly IUnitTestRunner _unit;
    private readonly IPerformanceTestRunner _performance;
    private readonly IRegressionTestRunner _regression;
    private readonly IBaselineStore _baselineStore;
    private readonly IMetricsRegistry _metrics;
    private readonly TestingOptions _options;
    private readonly ILogger<CompositeTestRunner> _logger;

    public CompositeTestRunner(
        IUnitTestRunner unit,
        IPerformanceTestRunner performance,
        IRegressionTestRunner regression,
        IBaselineStore baselineStore,
        IMetricsRegistry metrics,
        IOptions<TestingOptions> options,
        ILogger<CompositeTestRunner> logger)
    {
        _unit = unit;
        _performance = performance;
        _regression = regression;
        _baselineStore = baselineStore;
        _metrics = metrics;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TestSuite> RunAllTestsAsync(
        CycleContext context,
        CancellationToken ct = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        _logger.LogInformation("[Cycle {Id}] Démarrage des tests...", context.CycleId);

        // 1. Tests unitaires
        var unitSw = Stopwatch.StartNew();
        var unitResults = await _unit.RunAsync(_options.TestProjectPath, ct);
        _metrics.RecordTestDuration("unit", unitSw.Elapsed);

        _logger.LogDebug("Tests unitaires : {Passed}/{Total} passés.", unitResults.Passed, unitResults.TotalTests);

        // 2. Benchmarks performance
        var perfSw = Stopwatch.StartNew();
        var perfResults = await _performance.RunAsync(_options.TestProjectPath, ct);
        _metrics.RecordTestDuration("performance", perfSw.Elapsed);

        _logger.LogDebug("Benchmarks : {Count} exécutés.", perfResults.Benchmarks.Count);

        // 3. Normalisation des scores par rapport à la baseline
        var baseline = await _baselineStore.GetLatestBaselineAsync(ct);
        perfResults = NormalizePerformanceScores(perfResults, baseline);

        // 4. Tests de régression
        var regSw = Stopwatch.StartNew();
        var regResults = await _regression.RunAsync(baseline, new TestSuite
        {
            CycleId = context.CycleId,
            RecordedAt = startedAt,
            UnitTests = unitResults,
            Performance = perfResults,
            Regression = new RegressionResults { Checks = [] } // placeholder
        }, ct);
        _metrics.RecordTestDuration("regression", regSw.Elapsed);

        var suite = new TestSuite
        {
            CycleId = context.CycleId,
            RecordedAt = DateTimeOffset.UtcNow,
            UnitTests = unitResults,
            Performance = perfResults,
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

    private static PerformanceResults NormalizePerformanceScores(
        PerformanceResults current, TestSuite? baseline)
    {
        if (baseline is null) return current;

        var normalizedBenchmarks = current.Benchmarks.Select(bench =>
        {
            var baselineBench = baseline.Performance.Benchmarks
                .FirstOrDefault(b => b.BenchmarkName == bench.BenchmarkName);

            if (baselineBench is null || baselineBench.MeanNanoseconds <= 0)
                return bench;

            // NormalizedScore > 1.0 = plus rapide que la baseline (amélioration)
            // NormalizedScore < 1.0 = plus lent que la baseline (régression)
            var normalizedScore = baselineBench.MeanNanoseconds / bench.MeanNanoseconds;

            return bench with { NormalizedScore = normalizedScore };
        }).ToList();

        return new PerformanceResults { Benchmarks = normalizedBenchmarks };
    }
}
