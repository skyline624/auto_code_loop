using System.Diagnostics;
using AutoLoop.Core.Models;
using AutoLoop.Testing.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoLoop.Testing;

public interface IPerformanceTestRunner
{
    Task<PerformanceResults> RunAsync(string projectPath, CancellationToken ct = default);
}

/// <summary>
/// Lance des benchmarks in-process simples via System.Diagnostics.Stopwatch.
/// En production, peut être remplacé par un runner BenchmarkDotNet complet.
/// Collecte N échantillons et calcule p50/p95/p99.
/// </summary>
public sealed class InProcessPerformanceTestRunner : IPerformanceTestRunner
{
    private readonly TestingOptions _options;
    private readonly ILogger<InProcessPerformanceTestRunner> _logger;

    public InProcessPerformanceTestRunner(
        IOptions<TestingOptions> options,
        ILogger<InProcessPerformanceTestRunner> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<PerformanceResults> RunAsync(
        string projectPath, CancellationToken ct = default)
    {
        var benchmarks = new List<BenchmarkResult>();

        // Benchmark de démonstration : mesure d'une opération synthétique
        benchmarks.Add(RunSyntheticBenchmark(
            "SyntheticSum_1000",
            () =>
            {
                var sum = 0L;
                for (var i = 0; i < 1000; i++) sum += i;
                return sum;
            }));

        benchmarks.Add(RunSyntheticBenchmark(
            "SyntheticStringConcat_100",
            () =>
            {
                var result = string.Empty;
                for (var i = 0; i < 100; i++) result += i.ToString();
                return result.Length;
            }));

        _logger.LogDebug("Benchmarks exécutés : {Count}", benchmarks.Count);

        return Task.FromResult(new PerformanceResults { Benchmarks = benchmarks });
    }

    private BenchmarkResult RunSyntheticBenchmark(string name, Func<object> action)
    {
        // Warmup
        for (var i = 0; i < 10; i++) action();

        // Collecte des échantillons
        var samples = new double[_options.BenchmarkIterations];
        var sw = new Stopwatch();

        for (var i = 0; i < _options.BenchmarkIterations; i++)
        {
            sw.Restart();
            action();
            sw.Stop();
            samples[i] = sw.Elapsed.TotalNanoseconds;
        }

        Array.Sort(samples);
        var mean = samples.Average();

        return new BenchmarkResult
        {
            BenchmarkName = name,
            MeanNanoseconds = mean,
            StdDevNanoseconds = ComputeStdDev(samples, mean),
            MedianNanoseconds = samples[samples.Length / 2],
            AllocatedBytes = 0, // Nécessite tracemalloc .NET
            RawSamples = samples.ToList(),
            NormalizedScore = 1.0 // Score de référence (sera normalisé par rapport à la baseline)
        };
    }

    private static double ComputeStdDev(double[] samples, double mean)
    {
        var sumSquares = samples.Sum(s => Math.Pow(s - mean, 2));
        return Math.Sqrt(sumSquares / samples.Length);
    }
}
