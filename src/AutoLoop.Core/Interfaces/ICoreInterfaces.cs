using AutoLoop.Core.Models;

namespace AutoLoop.Core.Interfaces;

// === Interfaces Claude Code (NOUVEAU) ===

/// <summary>
/// Exécuteur pour invoquer Claude Code CLI via subprocess.
/// </summary>
public interface IClaudeCodeExecutor
{
    /// <summary>
    /// Exécute Claude Code avec un prompt donné.
    /// </summary>
    Task<ClaudeCodeResult> ExecuteAsync(
        string prompt,
        IEnumerable<string>? contextFiles = null,
        IReadOnlyDictionary<string, string>? variables = null,
        CancellationToken ct = default);

    /// <summary>
    /// Exécute Claude Code avec demande de confirmation interactive.
    /// </summary>
    Task<ClaudeCodeResult> ExecuteWithConfirmationAsync(
        string prompt,
        string confirmationPrompt,
        IEnumerable<string>? contextFiles = null,
        CancellationToken ct = default);
}

/// <summary>
/// Détecteur de type de projet (Python, Node.js, .NET, Rust, Go, etc.).
/// </summary>
public interface IProjectDetector
{
    /// <summary>
    /// Détecte le type de projet à partir du chemin donné.
    /// </summary>
    Task<ProjectInfo> DetectAsync(string projectPath, CancellationToken ct = default);

    /// <summary>
    /// Détecte si un framework de test est présent.
    /// </summary>
    Task<bool> DetectTestFrameworkAsync(string projectPath, CancellationToken ct = default);
}

/// <summary>
/// Préservateur d'intention utilisateur à travers les cycles.
/// </summary>
public interface IIntentPreserver
{
    /// <summary>
    /// Stocke l'intention utilisateur pour un cycle.
    /// </summary>
    Task StoreIntentAsync(CycleId cycleId, UserIntent intent, CancellationToken ct = default);

    /// <summary>
    /// Récupère l'intention utilisateur pour un cycle.
    /// </summary>
    Task<UserIntent?> GetIntentAsync(CycleId cycleId, CancellationToken ct = default);

    /// <summary>
    /// Génère un prompt de contexte incluant l'historique des cycles.
    /// </summary>
    Task<string> GenerateContextPromptAsync(CycleId cycleId, CancellationToken ct = default);
}

/// <summary>
/// Mémoire persistante entre les cycles d'amélioration.
/// </summary>
public interface ICycleMemory
{
    /// <summary>
    /// Récupère les N derniers cycles pour le contexte.
    /// </summary>
    Task<IReadOnlyList<CycleSummary>> GetRecentCyclesAsync(int limit = 10, CancellationToken ct = default);

    /// <summary>
    /// Stocke le résumé d'un cycle.
    /// </summary>
    Task StoreCycleSummaryAsync(CycleSummary summary, CancellationToken ct = default);
}

// === Interfaces existantes (CONSERVÉES) ===

public interface IHypothesisEngine
{
    Task<IReadOnlyList<Hypothesis>> GenerateHypothesesAsync(
        CycleContext context,
        CancellationToken ct = default);
}

public interface IMutationEngine
{
    Task<ChangeRecord> ApplyMutationAsync(
        Hypothesis hypothesis,
        CycleContext context,
        CancellationToken ct = default);

    Task RevertMutationAsync(
        ChangeRecord change,
        CancellationToken ct = default);
}

public interface ITestRunner
{
    Task<TestSuite> RunAllTestsAsync(
        CycleContext context,
        CancellationToken ct = default);
}

public interface IEvaluationEngine
{
    Task<EvaluationResult> EvaluateAsync(
        TestSuite? baseline,
        TestSuite candidate,
        CycleContext context,
        CancellationToken ct = default);
}

public interface IVersioningBackend
{
    Task<string> CreateBranchAsync(string branchName, CancellationToken ct = default);

    Task<string> CommitChangesAsync(
        ChangeRecord change,
        string branchName,
        CancellationToken ct = default);

    Task<string> CreatePullRequestAsync(CycleContext context, CancellationToken ct = default);

    Task MergePullRequestAsync(string prUrl, CancellationToken ct = default);

    Task DeleteBranchAsync(string branchName, CancellationToken ct = default);

    Task<IReadOnlyList<VersionEntry>> ListVersionHistoryAsync(CancellationToken ct = default);
}

public interface IRollbackManager
{
    Task<RollbackResult> RollbackAsync(
        CycleContext context,
        RollbackReason reason,
        CancellationToken ct = default);
}

public interface IBaselineStore
{
    Task<TestSuite?> GetLatestBaselineAsync(CancellationToken ct = default);
    Task StoreBaselineAsync(TestSuite suite, CancellationToken ct = default);
}

public interface ICycleJournal
{
    Task BeginCycleAsync(CycleContext context, CancellationToken ct = default);
    Task EndCycleAsync(CycleContext context, CancellationToken ct = default);
    Task<IReadOnlyList<object>> GetRecentCyclesAsync(int limit = 20, CancellationToken ct = default);
}

public interface IMetricsRegistry
{
    void RecordCycleStarted(CycleId cycleId);
    void RecordCycleCompleted(CycleId cycleId, CycleStatus status, TimeSpan duration);
    void RecordTestDuration(string testType, TimeSpan duration);
    void RecordHypothesisGenerated(int count);
    void RecordDecision(DecisionOutcome outcome);
    void RecordGitHubApiCall(string operation, bool success, TimeSpan duration);
    void RecordRollback(RollbackReason reason);

    // NOUVEAU: Métriques Claude Code
    void RecordClaudeCodeCall(CyclePhase phase, bool success, TimeSpan duration, int inputTokens, int outputTokens);
}

public interface IAlertManager
{
    Task SendAlertAsync(
        AlertSeverity severity,
        string title,
        string message,
        CancellationToken ct = default);
}
