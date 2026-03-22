namespace AutoLoop.Core.Options;

public sealed class CycleOptions
{
    public const string Section = "Cycle";
    public int CycleIntervalMs { get; set; } = 60_000;
    public int MaxHypothesesPerCycle { get; set; } = 3;
    public bool DryRun { get; set; } = false;
    public string TargetProjectPath { get; set; } = "./src";
    public int? MaxCycles { get; set; } = null;
    public bool InteractiveMode { get; set; } = false;
}

public sealed class StorageOptions
{
    public const string Section = "Storage";
    public string JournalsPath { get; set; } = "./storage/journals";
    public string AuditTrailPath { get; set; } = "./storage/audit.jsonl";
    public string BaselinePath { get; set; } = "./storage/baseline.json";
    public string ChangeTrackerPath { get; set; } = "./storage/changes.jsonl";
    public string MemoryPath { get; set; } = "./storage/memory";
    public string PromptsPath { get; set; } = "./storage/prompts";
}
