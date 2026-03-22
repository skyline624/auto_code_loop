namespace AutoLoop.Core.Models;

public sealed record EvaluationResult
{
    public required CycleId CycleId { get; init; }
    public required DecisionOutcome Decision { get; init; }
    public required string DecisionRationale { get; init; }
    public required IReadOnlyList<StatisticalTestResult> StatisticalTests { get; init; }
    public required double OverallImprovementScore { get; init; }
    public required ThresholdComparison ThresholdComparison { get; init; }
    public required DateTimeOffset EvaluatedAt { get; init; }
}

public sealed record StatisticalTestResult
{
    public required string TestName { get; init; }
    public required double Statistic { get; init; }
    public required double PValue { get; init; }
    public required bool IsSignificant { get; init; }
    public required double EffectSize { get; init; }
    public required (double Lower, double Upper) ConfidenceInterval { get; init; }
}

public sealed record ThresholdComparison
{
    public required bool UnitTestsPassed { get; init; }
    public required bool RegressionPassed { get; init; }
    public required bool PerformanceImproved { get; init; }
    public required bool StatisticallySignificant { get; init; }
    public required double ActualImprovementPercent { get; init; }
    public required double RequiredImprovementPercent { get; init; }
}

public sealed record RollbackResult
{
    public required bool Succeeded { get; init; }
    public required RollbackReason Reason { get; init; }
    public required RollbackStrategy StrategyUsed { get; init; }
    public required DateTimeOffset ExecutedAt { get; init; }
    public required TimeSpan Duration { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed record VersionEntry
{
    public required string CommitSha { get; init; }
    public required string Message { get; init; }
    public required DateTimeOffset CommittedAt { get; init; }
    public required string Author { get; init; }
    public string? BranchName { get; init; }
    public string? PullRequestUrl { get; init; }
}

public sealed record HealthCheckResult
{
    public required bool IsHealthy { get; init; }
    public required IReadOnlyList<string> FailedChecks { get; init; }
    public required TimeSpan Duration { get; init; }
}
