using AutoLoop.Core.Models;
using AutoLoop.Evaluation;
using AutoLoop.Evaluation.Options;
using FluentAssertions;
using Xunit;

namespace AutoLoop.Tests.Evaluation;

public sealed class DecisionEngineTests
{
    private readonly ConservativeDecisionEngine _sut = new();

    private static EvaluationOptions DefaultOptions() => new()
    {
        StatisticalSignificanceAlpha = 0.05,
        MinCohensD = 0.2,
        RequireBootstrapCIPositive = true,
        MinPerformanceImprovementPercent = 5.0,
        RequireAllUnitTestsPassing = true
    };

    private static StatisticalTestResult SignificantTest(string name) => new()
    {
        TestName = name,
        Statistic = 5.0,
        PValue = 0.001,
        IsSignificant = true,
        EffectSize = 0.8,
        ConfidenceInterval = (-50, -10)
    };

    private static StatisticalTestResult InsignificantTest(string name) => new()
    {
        TestName = name,
        Statistic = 0.1,
        PValue = 0.9,
        IsSignificant = false,
        EffectSize = 0.05,
        ConfidenceInterval = (-5, 10)
    };

    [Fact]
    public void Decide_AllConditionsMet_ReturnsAccept()
    {
        var comparison = new ThresholdComparison
        {
            UnitTestsPassed = true,
            RegressionPassed = true,
            PerformanceImproved = true,
            StatisticallySignificant = true,
            ActualImprovementPercent = 10.0,
            RequiredImprovementPercent = 5.0
        };

        var tests = new List<StatisticalTestResult>
        {
            SignificantTest("Welch t-test"),
            new() { TestName = "Cohen's d", Statistic = 0.8, PValue = double.NaN, IsSignificant = true, EffectSize = 0.8, ConfidenceInterval = (double.NaN, double.NaN) },
            new() { TestName = "Bootstrap CI (95%)", Statistic = -20, PValue = 0.01, IsSignificant = true, EffectSize = -20, ConfidenceInterval = (-30, -10) }
        };

        var result = _sut.Decide(comparison, tests, DefaultOptions());

        result.Should().Be(DecisionOutcome.Accept);
    }

    [Fact]
    public void Decide_UnitTestsFailed_ReturnsReject()
    {
        var comparison = new ThresholdComparison
        {
            UnitTestsPassed = false,
            RegressionPassed = true,
            PerformanceImproved = true,
            StatisticallySignificant = true,
            ActualImprovementPercent = 10.0,
            RequiredImprovementPercent = 5.0
        };

        var result = _sut.Decide(comparison, [], DefaultOptions());

        result.Should().Be(DecisionOutcome.Reject);
    }

    [Fact]
    public void Decide_RegressionDetected_ReturnsReject()
    {
        var comparison = new ThresholdComparison
        {
            UnitTestsPassed = true,
            RegressionPassed = false,
            PerformanceImproved = false,
            StatisticallySignificant = false,
            ActualImprovementPercent = -5.0,
            RequiredImprovementPercent = 5.0
        };

        var result = _sut.Decide(comparison, [], DefaultOptions());

        result.Should().Be(DecisionOutcome.Reject);
    }

    [Fact]
    public void Decide_InsufficientImprovement_ReturnsReject()
    {
        var comparison = new ThresholdComparison
        {
            UnitTestsPassed = true,
            RegressionPassed = true,
            PerformanceImproved = true,
            StatisticallySignificant = true,
            ActualImprovementPercent = 1.0, // Trop faible
            RequiredImprovementPercent = 5.0
        };

        var tests = new List<StatisticalTestResult>
        {
            SignificantTest("Welch t-test"),
            new() { TestName = "Cohen's d", Statistic = 0.5, PValue = double.NaN, IsSignificant = true, EffectSize = 0.5, ConfidenceInterval = (double.NaN, double.NaN) },
            new() { TestName = "Bootstrap CI (95%)", Statistic = -5, PValue = 0.03, IsSignificant = true, EffectSize = -5, ConfidenceInterval = (-8, -2) }
        };

        var result = _sut.Decide(comparison, tests, DefaultOptions());

        result.Should().Be(DecisionOutcome.Reject);
    }

    [Fact]
    public void Decide_StatisticallySignificantButSmallEffect_ReturnsDefer()
    {
        var comparison = new ThresholdComparison
        {
            UnitTestsPassed = true,
            RegressionPassed = true,
            PerformanceImproved = false,
            StatisticallySignificant = true,
            ActualImprovementPercent = 3.0,
            RequiredImprovementPercent = 5.0
        };

        var tests = new List<StatisticalTestResult>
        {
            SignificantTest("Welch t-test"),
            new() { TestName = "Cohen's d", Statistic = 0.1, PValue = double.NaN, IsSignificant = false, EffectSize = 0.1, ConfidenceInterval = (double.NaN, double.NaN) }
        };

        var result = _sut.Decide(comparison, tests, DefaultOptions());

        result.Should().Be(DecisionOutcome.Defer);
    }
}
