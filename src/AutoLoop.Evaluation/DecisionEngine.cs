using AutoLoop.Core.Models;
using AutoLoop.Evaluation.Options;
using Microsoft.Extensions.Options;

namespace AutoLoop.Evaluation;

/// <summary>
/// Moteur de décision conservateur.
/// Hard gates : si l'un échoue → REJECT immédiat.
/// Soft acceptance : tous les critères doivent passer → ACCEPT.
/// Sinon → DEFER.
/// </summary>
public interface IDecisionEngine
{
    DecisionOutcome Decide(
        ThresholdComparison comparison,
        IReadOnlyList<StatisticalTestResult> tests,
        EvaluationOptions thresholds);
}

public sealed class ConservativeDecisionEngine : IDecisionEngine
{
    public DecisionOutcome Decide(
        ThresholdComparison comparison,
        IReadOnlyList<StatisticalTestResult> tests,
        EvaluationOptions thresholds)
    {
        // ── Hard gates : REJECT immédiat si l'un échoue ──────────────────────
        if (thresholds.RequireAllUnitTestsPassing && !comparison.UnitTestsPassed)
            return DecisionOutcome.Reject;

        if (!comparison.RegressionPassed)
            return DecisionOutcome.Reject;

        // ── Soft acceptance : ACCEPT si tous les critères positifs ───────────
        var statSignificant = tests.Any(t =>
            !double.IsNaN(t.PValue) && t.IsSignificant);

        var meaningfulEffect = tests
            .Where(t => t.TestName == "Cohen's d")
            .Any(t => Math.Abs(t.EffectSize) >= thresholds.MinCohensD);

        var bootstrapPositive = !thresholds.RequireBootstrapCIPositive
            || tests.Where(t => t.TestName.StartsWith("Bootstrap"))
                    .Any(t => t.IsSignificant);

        var sufficientImprovement =
            comparison.ActualImprovementPercent >= comparison.RequiredImprovementPercent;

        if (statSignificant && meaningfulEffect && bootstrapPositive && sufficientImprovement)
            return DecisionOutcome.Accept;

        // ── DEFER : données insuffisantes mais pas de régression ─────────────
        if (statSignificant && !meaningfulEffect)
            return DecisionOutcome.Defer;

        return DecisionOutcome.Reject;
    }
}
