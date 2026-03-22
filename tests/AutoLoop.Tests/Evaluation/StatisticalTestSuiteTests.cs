using AutoLoop.Evaluation;
using FluentAssertions;
using Xunit;

namespace AutoLoop.Tests.Evaluation;

public sealed class StatisticalTestSuiteTests
{
    private readonly MathNetStatisticalTestSuite _sut = new();

    [Fact]
    public void WelchTTest_SignificantDifference_ReturnsIsSignificantTrue()
    {
        // Arrange : deux distributions clairement différentes
        var baseline = Enumerable.Range(0, 50).Select(i => 100.0 + i * 0.1).ToList();
        var candidate = Enumerable.Range(0, 50).Select(i => 80.0 + i * 0.1).ToList(); // 20% plus rapide

        // Act
        var result = _sut.RunWelchTTest(baseline, candidate, alpha: 0.05);

        // Assert
        result.IsSignificant.Should().BeTrue();
        result.PValue.Should().BeLessThan(0.05);
        result.TestName.Should().Be("Welch t-test");
    }

    [Fact]
    public void WelchTTest_IdenticalSamples_ReturnsNotSignificant()
    {
        var samples = Enumerable.Range(0, 30).Select(i => 100.0 + i).ToList();

        var result = _sut.RunWelchTTest(samples, samples, alpha: 0.05);

        // Même distribution → p-value très haute
        result.IsSignificant.Should().BeFalse();
    }

    [Fact]
    public void MannWhitneyU_SignificantDifference_ReturnsIsSignificantTrue()
    {
        var baseline = Enumerable.Range(0, 30).Select(i => 200.0 + i).ToList();
        var candidate = Enumerable.Range(0, 30).Select(i => 100.0 + i).ToList();

        var result = _sut.RunMannWhitneyU(baseline, candidate, alpha: 0.05);

        result.IsSignificant.Should().BeTrue();
        result.TestName.Should().Be("Mann-Whitney U");
    }

    [Fact]
    public void CohensD_LargeDifference_ReturnsLargeEffect()
    {
        // baseline élevé, candidat bas → grande amélioration (variance non-nulle requise)
        var baseline = Enumerable.Range(0, 50).Select(i => 200.0 + i * 0.1).ToList();
        var candidate = Enumerable.Range(0, 50).Select(i => 100.0 + i * 0.1).ToList();

        var result = _sut.ComputeCohensD(baseline, candidate);

        // d < 0 car candidat < baseline en latence (amélioration)
        Math.Abs(result.EffectSize).Should().BeGreaterThan(0.2);
        result.IsSignificant.Should().BeTrue();
    }

    [Fact]
    public void CohensD_SmallDifference_ReturnsSmallEffect()
    {
        // Variance large (i * 1.0) et différence de moyenne infime (0.1) → d << 0.2
        var baseline = Enumerable.Range(0, 50).Select(i => 100.0 + i * 1.0).ToList();
        var candidate = Enumerable.Range(0, 50).Select(i => 100.1 + i * 1.0).ToList();

        var result = _sut.ComputeCohensD(baseline, candidate);

        Math.Abs(result.EffectSize).Should().BeLessThan(0.2);
        result.IsSignificant.Should().BeFalse();
    }

    [Fact]
    public void BootstrapCI_ClearImprovement_ReturnsPositiveLowerBound()
    {
        var baseline = Enumerable.Range(0, 50).Select(_ => 200.0).ToList();
        var candidate = Enumerable.Range(0, 50).Select(_ => 100.0).ToList();

        var result = _sut.ComputeBootstrapCI(baseline, candidate, iterations: 1000);

        // Candidat plus rapide → différence négative → amélioration
        result.ConfidenceInterval.Lower.Should().NotBe(double.NaN);
        result.ConfidenceInterval.Upper.Should().NotBe(double.NaN);
    }

    [Fact]
    public void WelchTTest_InsufficientSamples_ReturnsNotSignificant()
    {
        var tiny = new List<double> { 1.0 };

        var result = _sut.RunWelchTTest(tiny, tiny, alpha: 0.05);

        result.IsSignificant.Should().BeFalse();
    }
}
