namespace AutoLoop.Evaluation.Options;

public sealed class EvaluationOptions
{
    public const string Section = "Evaluation";

    // Seuils statistiques
    public double StatisticalSignificanceAlpha { get; set; } = 0.05;
    public double MinCohensD { get; set; } = 0.2;
    public bool RequireBootstrapCIPositive { get; set; } = true;
    public int BootstrapIterations { get; set; } = 10_000;

    // Seuils de performance
    public double MinPerformanceImprovementPercent { get; set; } = 5.0;
    public double MaxAllowedRegressionPercent { get; set; } = 1.0;

    // Gates de qualité
    public bool RequireAllUnitTestsPassing { get; set; } = true;
}
