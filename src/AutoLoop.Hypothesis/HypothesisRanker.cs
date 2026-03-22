using AutoLoop.Core.Models;
using HypothesisModel = AutoLoop.Core.Models.Hypothesis;

namespace AutoLoop.Hypothesis;

/// <summary>
/// Classe les hypothèses par score composite :
/// score = Priority*0.4 + ExpectedImpact*0.4 + ConfidenceScore*0.2
/// </summary>
public interface IHypothesisRanker
{
    IReadOnlyList<HypothesisModel> Rank(IReadOnlyList<HypothesisModel> hypotheses);
}

public sealed class WeightedHypothesisRanker : IHypothesisRanker
{
    private const double PriorityWeight = 0.4;
    private const double ImpactWeight = 0.4;
    private const double ConfidenceWeight = 0.2;

    public IReadOnlyList<HypothesisModel> Rank(IReadOnlyList<HypothesisModel> hypotheses)
        => hypotheses
            .OrderByDescending(h =>
                h.Priority * PriorityWeight +
                h.ExpectedImpact * ImpactWeight +
                h.ConfidenceScore * ConfidenceWeight)
            .ToList();
}
