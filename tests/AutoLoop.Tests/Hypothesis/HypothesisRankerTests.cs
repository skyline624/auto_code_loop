using AutoLoop.Core.Models;
using AutoLoop.Hypothesis;
using FluentAssertions;
using Xunit;

using HypothesisModel = AutoLoop.Core.Models.Hypothesis;

namespace AutoLoop.Tests.Hypothesis;

public sealed class HypothesisRankerTests
{
    private readonly WeightedHypothesisRanker _sut = new();

    private static HypothesisModel MakeHypothesis(double priority, double impact, double confidence) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            CycleId = CycleId.New(),
            Type = HypothesisType.PerformanceBottleneck,
            TargetFile = "test.cs",
            Rationale = "Test",
            Priority = priority,
            ExpectedImpact = impact,
            ConfidenceScore = confidence,
            Evidence = new Dictionary<string, object>(),
            GeneratedAt = DateTimeOffset.UtcNow
        };

    [Fact]
    public void Rank_ReturnsHighestScoredFirst()
    {
        var low = MakeHypothesis(0.1, 0.1, 0.1);     // score ~0.1
        var high = MakeHypothesis(0.9, 0.9, 0.9);    // score ~0.9
        var medium = MakeHypothesis(0.5, 0.5, 0.5);  // score ~0.5

        var ranked = _sut.Rank([low, medium, high]);

        ranked[0].Priority.Should().Be(high.Priority);
        ranked[1].Priority.Should().Be(medium.Priority);
        ranked[2].Priority.Should().Be(low.Priority);
    }

    [Fact]
    public void Rank_EmptyList_ReturnsEmpty()
    {
        var ranked = _sut.Rank([]);
        ranked.Should().BeEmpty();
    }

    [Fact]
    public void Rank_SingleHypothesis_ReturnsSingleItem()
    {
        var h = MakeHypothesis(0.5, 0.5, 0.5);
        var ranked = _sut.Rank([h]);
        ranked.Should().HaveCount(1);
    }
}
