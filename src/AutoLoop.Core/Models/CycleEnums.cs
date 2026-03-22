namespace AutoLoop.Core.Models;

public enum CyclePhase
{
    HypothesisGeneration,
    ChangeApplication,
    ExhaustiveTesting,
    DecisionAndComparison,
    Completed,
    Failed,
    RolledBack
}

public enum CycleStatus
{
    Running,
    Succeeded,
    Failed,
    Rejected,
    Deferred,
    RolledBack
}

public enum MutationType
{
    Refactoring,
    PerformanceOptimization,
    BugFix,
    AlgorithmReplacement,
    CacheIntroduction,
    ParallelizationIntroduction,
    MemoryOptimization
}

public enum HypothesisType
{
    PerformanceBottleneck,
    RecurringError,
    CoverageGap,
    CodeSmell,
    MemoryLeak,
    UnoptimizedAlgorithm
}

public enum DecisionOutcome
{
    Accept,
    Reject,
    Defer
}

public enum RollbackReason
{
    EvaluationFailed,
    TestsFailed,
    UnhandledException,
    HealthCheckFailed,
    ManualTrigger
}

public enum RollbackStrategy
{
    InMemoryRestore,
    GitCheckout,
    GitRevert
}

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}
