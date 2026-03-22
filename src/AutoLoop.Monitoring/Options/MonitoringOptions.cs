namespace AutoLoop.Monitoring.Options;

public sealed class MonitoringOptions
{
    public const string Section = "Monitoring";
    public int PrometheusPort { get; set; } = 9090;
    public double MaxCpuPercent { get; set; } = 90.0;
    public long MaxMemoryMb { get; set; } = 2048;
    public int MaxConsecutiveFailures { get; set; } = 3;
}
