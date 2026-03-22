namespace AutoLoop.Core.Models;

public sealed record TestSuite
{
    public required CycleId CycleId { get; init; }
    public required DateTimeOffset RecordedAt { get; init; }
    public required UnitTestResults UnitTests { get; init; }
    public required PerformanceResults Performance { get; init; }
    public required RegressionResults Regression { get; init; }

    public bool AllPassed => UnitTests.AllPassed && Regression.AllPassed;

    public double OverallPerformanceScore =>
        Performance.Benchmarks.Count == 0
            ? 0.0
            : Performance.Benchmarks.Average(b => b.NormalizedScore);
}

public sealed record UnitTestResults
{
    public required int TotalTests { get; init; }
    public required int Passed { get; init; }
    public required int Failed { get; init; }
    public required int Skipped { get; init; }
    public required TimeSpan Duration { get; init; }
    public required IReadOnlyList<TestFailure> Failures { get; init; }
    public bool AllPassed => Failed == 0;
    public double PassRate => TotalTests == 0 ? 1.0 : (double)Passed / TotalTests;
}

public sealed record PerformanceResults
{
    public required IReadOnlyList<BenchmarkResult> Benchmarks { get; init; }
}

public sealed record BenchmarkResult
{
    public required string BenchmarkName { get; init; }
    public required double MeanNanoseconds { get; init; }
    public required double StdDevNanoseconds { get; init; }
    public required double MedianNanoseconds { get; init; }
    public required double AllocatedBytes { get; init; }
    public required IReadOnlyList<double> RawSamples { get; init; }
    /// <summary>Score normalisé [0,1] par rapport à la baseline. 1.0 = identique, >1.0 = amélioration.</summary>
    public required double NormalizedScore { get; init; }
}

public sealed record RegressionResults
{
    public required IReadOnlyList<RegressionCheck> Checks { get; init; }
    public bool AllPassed => Checks.All(c => c.Passed);
}

public sealed record RegressionCheck
{
    public required string CheckName { get; init; }
    public required bool Passed { get; init; }
    public string? FailureReason { get; init; }
}

public sealed record TestFailure
{
    public required string TestName { get; init; }
    public required string Message { get; init; }
    public required string StackTrace { get; init; }
}
