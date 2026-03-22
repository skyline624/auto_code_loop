namespace AutoLoop.Hypothesis.Options;

public sealed class HypothesisOptions
{
    public const string Section = "Hypothesis";
    public int LookbackCycles { get; set; } = 10;
    public int MaxHypotheses { get; set; } = 5;
    public double MinConfidenceThreshold { get; set; } = 0.3;
    public double MinDeltaPercentToFlag { get; set; } = 5.0;
}
