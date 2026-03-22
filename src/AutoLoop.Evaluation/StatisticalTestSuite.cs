using AutoLoop.Core.Models;
using MathNet.Numerics.Distributions;
using MathNet.Numerics.Statistics;

namespace AutoLoop.Evaluation;

/// <summary>
/// Suite de tests statistiques basée sur MathNet.Numerics.
/// Implémente : t-test de Welch, Mann-Whitney U, Cohen's d, Bootstrap CI.
/// </summary>
public interface IStatisticalTestSuite
{
    StatisticalTestResult RunWelchTTest(
        IReadOnlyList<double> baseline,
        IReadOnlyList<double> candidate,
        double alpha = 0.05);

    StatisticalTestResult RunMannWhitneyU(
        IReadOnlyList<double> baseline,
        IReadOnlyList<double> candidate,
        double alpha = 0.05);

    StatisticalTestResult ComputeCohensD(
        IReadOnlyList<double> baseline,
        IReadOnlyList<double> candidate);

    StatisticalTestResult ComputeBootstrapCI(
        IReadOnlyList<double> baseline,
        IReadOnlyList<double> candidate,
        int iterations = 10_000,
        double confidence = 0.95);
}

public sealed class MathNetStatisticalTestSuite : IStatisticalTestSuite
{
    public StatisticalTestResult RunWelchTTest(
        IReadOnlyList<double> baseline,
        IReadOnlyList<double> candidate,
        double alpha = 0.05)
    {
        if (baseline.Count < 2 || candidate.Count < 2)
            return InsignificantResult("Welch t-test", "Échantillon trop petit");

        var baselineArr = baseline.ToArray();
        var candidateArr = candidate.ToArray();

        var baselineMean = Statistics.Mean(baselineArr);
        var candidateMean = Statistics.Mean(candidateArr);
        var baselineVar = Statistics.Variance(baselineArr);
        var candidateVar = Statistics.Variance(candidateArr);

        // t-test de Welch (variances inégales)
        var se = Math.Sqrt(baselineVar / baseline.Count + candidateVar / candidate.Count);
        if (se < 1e-10) return InsignificantResult("Welch t-test", "Variance nulle");

        var tStat = (candidateMean - baselineMean) / se;

        // Degrés de liberté de Welch-Satterthwaite
        var df = Math.Pow(baselineVar / baseline.Count + candidateVar / candidate.Count, 2) /
                 (Math.Pow(baselineVar / baseline.Count, 2) / (baseline.Count - 1) +
                  Math.Pow(candidateVar / candidate.Count, 2) / (candidate.Count - 1));

        var pValue = 2.0 * StudentT.CDF(0, 1, df, -Math.Abs(tStat));
        var effectSize = ComputeEffectSizeValue(baselineArr, candidateArr);

        var ci = ConfidenceInterval(baselineMean, candidateMean, se, df, 0.95);

        return new StatisticalTestResult
        {
            TestName = "Welch t-test",
            Statistic = tStat,
            PValue = pValue,
            IsSignificant = pValue < alpha,
            EffectSize = effectSize,
            ConfidenceInterval = ci
        };
    }

    public StatisticalTestResult RunMannWhitneyU(
        IReadOnlyList<double> baseline,
        IReadOnlyList<double> candidate,
        double alpha = 0.05)
    {
        if (baseline.Count < 2 || candidate.Count < 2)
            return InsignificantResult("Mann-Whitney U", "Échantillon trop petit");

        // Calcul du rang U
        var n1 = baseline.Count;
        var n2 = candidate.Count;

        var combined = baseline.Select((v, i) => (Value: v, Group: 0))
            .Concat(candidate.Select((v, i) => (Value: v, Group: 1)))
            .OrderBy(x => x.Value)
            .ToList();

        var rankSum = combined
            .Select((x, idx) => (x, Rank: idx + 1.0))
            .Where(x => x.x.Group == 1)
            .Sum(x => x.Rank);

        var u = rankSum - n2 * (n2 + 1) / 2.0;
        var meanU = n1 * n2 / 2.0;
        var stdU = Math.Sqrt(n1 * n2 * (n1 + n2 + 1) / 12.0);

        if (stdU < 1e-10) return InsignificantResult("Mann-Whitney U", "Variance nulle");

        var z = (u - meanU) / stdU;
        var pValue = 2.0 * Normal.CDF(0, 1, -Math.Abs(z));

        // Effet r = Z / sqrt(N)
        var effectSize = Math.Abs(z) / Math.Sqrt(n1 + n2);

        return new StatisticalTestResult
        {
            TestName = "Mann-Whitney U",
            Statistic = u,
            PValue = pValue,
            IsSignificant = pValue < alpha,
            EffectSize = effectSize,
            ConfidenceInterval = (double.NaN, double.NaN)
        };
    }

    public StatisticalTestResult ComputeCohensD(
        IReadOnlyList<double> baseline,
        IReadOnlyList<double> candidate)
    {
        if (baseline.Count < 2 || candidate.Count < 2)
            return InsignificantResult("Cohen's d", "Échantillon trop petit");

        var d = ComputeEffectSizeValue(baseline.ToArray(), candidate.ToArray());

        return new StatisticalTestResult
        {
            TestName = "Cohen's d",
            Statistic = d,
            PValue = double.NaN,
            IsSignificant = Math.Abs(d) >= 0.2, // Seuil minimal d'effet
            EffectSize = d,
            ConfidenceInterval = (double.NaN, double.NaN)
        };
    }

    public StatisticalTestResult ComputeBootstrapCI(
        IReadOnlyList<double> baseline,
        IReadOnlyList<double> candidate,
        int iterations = 10_000,
        double confidence = 0.95)
    {
        if (baseline.Count < 5 || candidate.Count < 5)
            return InsignificantResult("Bootstrap CI", "Échantillon trop petit");

        var rng = new Random(42); // Graine fixe pour reproductibilité
        var ba = baseline.ToArray();
        var ca = candidate.ToArray();
        var diffs = new double[iterations];

        for (var i = 0; i < iterations; i++)
        {
            var bMean = Enumerable.Range(0, ba.Length)
                .Select(_ => ba[rng.Next(ba.Length)]).Average();
            var cMean = Enumerable.Range(0, ca.Length)
                .Select(_ => ca[rng.Next(ca.Length)]).Average();
            diffs[i] = cMean - bMean;
        }

        Array.Sort(diffs);
        var alpha = 1.0 - confidence;
        var lower = diffs[(int)(iterations * alpha / 2)];
        var upper = diffs[(int)(iterations * (1 - alpha / 2))];
        var meanDiff = diffs.Average();
        var pValue = diffs.Count(d => d <= 0) / (double)iterations;

        return new StatisticalTestResult
        {
            TestName = $"Bootstrap CI ({confidence:P0})",
            Statistic = meanDiff,
            PValue = pValue,
            IsSignificant = lower > 0,
            EffectSize = meanDiff,
            ConfidenceInterval = (lower, upper)
        };
    }

    private static double ComputeEffectSizeValue(double[] baseline, double[] candidate)
    {
        var baselineMean = Statistics.Mean(baseline);
        var candidateMean = Statistics.Mean(candidate);
        var pooledStd = Math.Sqrt(
            (Statistics.Variance(baseline) * (baseline.Length - 1) +
             Statistics.Variance(candidate) * (candidate.Length - 1)) /
            (baseline.Length + candidate.Length - 2));

        if (pooledStd < 1e-10) return 0.0;
        return (candidateMean - baselineMean) / pooledStd;
    }

    private static (double Lower, double Upper) ConfidenceInterval(
        double baselineMean, double candidateMean, double se, double df, double confidence)
    {
        var alpha = 1.0 - confidence;
        var tCritical = StudentT.InvCDF(0, 1, df, 1.0 - alpha / 2);
        var diff = candidateMean - baselineMean;
        return (diff - tCritical * se, diff + tCritical * se);
    }

    private static StatisticalTestResult InsignificantResult(string testName, string reason) =>
        new()
        {
            TestName = testName,
            Statistic = 0,
            PValue = 1.0,
            IsSignificant = false,
            EffectSize = 0,
            ConfidenceInterval = (double.NaN, double.NaN)
        };
}
