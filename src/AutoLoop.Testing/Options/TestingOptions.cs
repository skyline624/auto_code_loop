namespace AutoLoop.Testing.Options;

public sealed class TestingOptions
{
    public const string Section = "Testing";
    public string TestProjectPath { get; set; } = "./tests";
    public int BenchmarkIterations { get; set; } = 100;
    public int TestTimeoutSeconds { get; set; } = 300;
    public int TestTimeoutMs { get; set; } = 300_000;
    public double MaxRegressionPercent { get; set; } = 5.0;
    public bool AutoDetectFramework { get; set; } = true;
    public bool FailFastOnErrors { get; set; } = true;
}
