using AutoLoop.Core.Exceptions;
using AutoLoop.Core.Interfaces;
using AutoLoop.Core.Models;
using AutoLoop.Core.Options;
using AutoLoop.Core.Prompts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoLoop.Core;

/// <summary>
/// Service hébergé qui pilote la boucle fermée d'auto-amélioration.
/// Séquence les 4 phases via Claude Code : Hypothesis → Mutation → Testing → Evaluation.
/// </summary>
public sealed class CycleOrchestrator : BackgroundService
{
    private readonly IClaudeCodeExecutor _claudeCodeExecutor;
    private readonly IProjectDetector _projectDetector;
    private readonly IIntentPreserver _intentPreserver;
    private readonly ICycleMemory _cycleMemory;
    private readonly ITestRunner _testRunner;
    private readonly IEvaluationEngine _evaluationEngine;
    private readonly IVersioningBackend _versioning;
    private readonly IRollbackManager _rollback;
    private readonly IMetricsRegistry _metrics;
    private readonly ICycleJournal _journal;
    private readonly IBaselineStore _baselineStore;
    private readonly IAlertManager _alertManager;
    private readonly IOptions<CycleOptions> _options;
    private readonly ILogger<CycleOrchestrator> _logger;
    private readonly UserIntent? _userIntent;

    private int _cycleCount;
    private int _consecutiveFailures;

    public CycleOrchestrator(
        IClaudeCodeExecutor claudeCodeExecutor,
        IProjectDetector projectDetector,
        IIntentPreserver intentPreserver,
        ICycleMemory cycleMemory,
        ITestRunner testRunner,
        IEvaluationEngine evaluationEngine,
        IVersioningBackend versioning,
        IRollbackManager rollback,
        IMetricsRegistry metrics,
        ICycleJournal journal,
        IBaselineStore baselineStore,
        IAlertManager alertManager,
        IOptions<CycleOptions> options,
        ILogger<CycleOrchestrator> logger,
        UserIntent? userIntent = null)
    {
        _claudeCodeExecutor = claudeCodeExecutor;
        _projectDetector = projectDetector;
        _intentPreserver = intentPreserver;
        _cycleMemory = cycleMemory;
        _testRunner = testRunner;
        _evaluationEngine = evaluationEngine;
        _versioning = versioning;
        _rollback = rollback;
        _metrics = metrics;
        _journal = journal;
        _baselineStore = baselineStore;
        _alertManager = alertManager;
        _options = options;
        _logger = logger;
        _userIntent = userIntent;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AutoLoop CycleOrchestrator démarré. DryRun={DryRun}", _options.Value.DryRun);

        // Détection du projet cible
        var projectPath = _options.Value.TargetProjectPath;
        _logger.LogInformation("Détection du projet cible: {ProjectPath}", projectPath);

        try
        {
            var project = await _projectDetector.DetectAsync(projectPath, stoppingToken);
            _logger.LogInformation("Projet détecté: {Type} ({Language}) - {PackageManager}",
                project.Type, project.Language ?? "N/A", project.PackageManager ?? "N/A");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de détecter le type de projet. Utilisation du mode générique.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_options.Value.MaxCycles.HasValue && _cycleCount >= _options.Value.MaxCycles.Value)
            {
                _logger.LogInformation("Nombre maximum de cycles ({Max}) atteint. Arrêt.", _options.Value.MaxCycles.Value);
                break;
            }

            var context = new CycleContext();
            _cycleCount++;
            _logger.LogInformation(
                "=== Début du cycle #{Num} [{CycleId}] ===",
                _cycleCount, context.CycleId);

            await RunCycleAsync(context, stoppingToken);

            if (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogDebug("Attente de {Ms}ms avant le prochain cycle.", _options.Value.CycleIntervalMs);
                await Task.Delay(_options.Value.CycleIntervalMs, stoppingToken);
            }
        }

        _logger.LogInformation("AutoLoop CycleOrchestrator arrêté après {Count} cycles.", _cycleCount);
    }

    private async Task RunCycleAsync(CycleContext context, CancellationToken ct)
    {
        _metrics.RecordCycleStarted(context.CycleId);
        await _journal.BeginCycleAsync(context, ct);

        try
        {
            // ── Phase 0 : Détection du projet et initialisation ────────────────────
            var projectPath = _options.Value.TargetProjectPath;
            context.Project = await _projectDetector.DetectAsync(projectPath, ct);

            // Initialiser l'intention utilisateur
            if (_userIntent != null)
            {
                context.UserIntent = _userIntent with { CycleId = context.CycleId };
                await _intentPreserver.StoreIntentAsync(context.CycleId, context.UserIntent, ct);
            }

            // Charger l'historique des cycles
            context.PreviousCycles = await _cycleMemory.GetRecentCyclesAsync(10, ct);

            _logger.LogInformation("[Cycle {Id}] Projet: {Type}, Intention: {Intent}",
                context.CycleId,
                context.Project?.Type ?? ProjectType.Unknown,
                context.UserIntent?.OriginalIntent ?? "N/A");

            // ── Phase 1 : Génération d'hypothèses via Claude Code ────────────────────
            context.CurrentPhase = CyclePhase.HypothesisGeneration;
            _logger.LogInformation("[Cycle {Id}] Phase 1 : Génération d'hypothèses via Claude Code", context.CycleId);

            var hypothesisPrompt = PromptTemplates.GenerateHypotheses(
                context.UserIntent ?? new UserIntent
                {
                    OriginalIntent = "Improve the codebase",
                    ExpandedIntent = "Improve the codebase",
                    CycleId = context.CycleId,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                context.Project!,
                context.PreviousCycles,
                metrics: null);

            var hypothesisResult = await _claudeCodeExecutor.ExecuteAsync(
                hypothesisPrompt,
                context.Project?.SourcePatterns,
                ct: ct);

            context.LastClaudeCodeResult = hypothesisResult;

            if (!hypothesisResult.Success)
            {
                _logger.LogError("[Cycle {Id}] Échec de la génération d'hypothèses: {Error}",
                    context.CycleId, hypothesisResult.ErrorOutput);
                context.Status = CycleStatus.Failed;
                context.ErrorMessage = hypothesisResult.ErrorOutput;
                return;
            }

            // Parser les hypothèses
            var parser = new ResponseParser();
            var hypothesisResponse = parser.ParseHypothesisResponse(hypothesisResult.Output);

            if (hypothesisResponse == null || hypothesisResponse.Hypotheses.Count == 0)
            {
                _logger.LogInformation("[Cycle {Id}] Aucune hypothèse générée — cycle ignoré.", context.CycleId);
                context.Status = CycleStatus.Succeeded;
                _consecutiveFailures = 0;
                return;
            }

            // Convertir en modèles domaine
            context.Hypotheses = parser.ToHypotheses(hypothesisResponse, context.CycleId);

            _metrics.RecordHypothesisGenerated(context.Hypotheses.Count);
            _logger.LogInformation(
                "[Cycle {Id}] {Count} hypothèse(s) générée(s). Sélectionnée : {Description}",
                context.CycleId, context.Hypotheses.Count,
                context.Hypotheses[0].Rationale[..Math.Min(100, context.Hypotheses[0].Rationale.Length)]);

            if (_options.Value.DryRun)
            {
                _logger.LogWarning("[Cycle {Id}] Mode DryRun — phases 2-4 ignorées.", context.CycleId);
                context.Status = CycleStatus.Succeeded;
                _consecutiveFailures = 0;
                return;
            }

            // ── Phase 2 : Application de la mutation via Claude Code ──────────────
            context.CurrentPhase = CyclePhase.ChangeApplication;
            _logger.LogInformation("[Cycle {Id}] Phase 2 : Application de la mutation", context.CycleId);

            await _versioning.CreateBranchAsync(context.CycleId.BranchName, ct);

            var topHypothesis = context.Hypotheses[0];
            var mutationPrompt = PromptTemplates.ApplyMutation(topHypothesis, context.Project!);

            // Lire le contenu original AVANT que Claude Code ne modifie le fichier
            var originalContent = "";
            if (topHypothesis.TargetFile != null && File.Exists(topHypothesis.TargetFile))
            {
                try { originalContent = await File.ReadAllTextAsync(topHypothesis.TargetFile, ct); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Cycle {Id}] Impossible de lire le fichier original : {File}",
                        context.CycleId, topHypothesis.TargetFile);
                }
            }

            var mutationResult = await _claudeCodeExecutor.ExecuteAsync(
                mutationPrompt,
                topHypothesis.TargetFile != null ? [topHypothesis.TargetFile] : null,
                ct: ct);

            if (!mutationResult.Success)
            {
                _logger.LogError("[Cycle {Id}] Échec de la mutation: {Error}",
                    context.CycleId, mutationResult.ErrorOutput);
                context.Status = CycleStatus.Failed;
                context.ErrorMessage = mutationResult.ErrorOutput;
                await ExecuteRollbackAsync(context, RollbackReason.UnhandledException, ct);
                return;
            }

            // Parser la mutation et créer le ChangeRecord
            var mutationResponse = parser.ParseMutationResponse(mutationResult.Output);
            if (mutationResponse == null || mutationResponse.Changes.Count == 0)
            {
                _logger.LogWarning("[Cycle {Id}] Aucune mutation générée.", context.CycleId);
                context.Status = CycleStatus.Failed;
                context.ErrorMessage = "No mutation generated";
                await ExecuteRollbackAsync(context, RollbackReason.UnhandledException, ct);
                return;
            }

            // Créer le ChangeRecord
            var firstChange = mutationResponse.Changes[0];
            context.AppliedChange = new ChangeRecord
            {
                Id = Guid.NewGuid(),
                CycleId = context.CycleId,
                HypothesisId = topHypothesis.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                FilePath = firstChange.FilePath,
                OriginalContent = originalContent,
                MutatedContent = firstChange.NewContent ?? "",
                UnifiedDiff = firstChange.Diff ?? "",
                MutationType = MutationType.Refactoring,
                Rationale = topHypothesis.Rationale,
                ClaudeCodePrompt = mutationPrompt,
                ClaudeCodeResponse = mutationResult.Output
            };

            var commitSha = await _versioning.CommitChangesAsync(
                context.AppliedChange, context.CycleId.BranchName, ct);

            context.AppliedChange = context.AppliedChange with { CommitSha = commitSha };

            _logger.LogInformation(
                "[Cycle {Id}] Mutation appliquée. ChangeId={ChangeId}, CommitSha={Sha}",
                context.CycleId, context.AppliedChange.Id, commitSha);

            // ── Phase 3 : Tests exhaustifs ────────────────────────────────────
            context.CurrentPhase = CyclePhase.ExhaustiveTesting;
            _logger.LogInformation("[Cycle {Id}] Phase 3 : Tests exhaustifs", context.CycleId);

            var baseline = await _baselineStore.GetLatestBaselineAsync(ct);
            context.TestResults = await _testRunner.RunAllTestsAsync(context, ct);

            _metrics.RecordTestDuration("all", context.TestResults.UnitTests.Duration);

            _logger.LogInformation(
                "[Cycle {Id}] Tests terminés. AllPassed={Passed}, UnitPass={Unit}/{Total}",
                context.CycleId,
                context.TestResults.AllPassed,
                context.TestResults.UnitTests.Passed,
                context.TestResults.UnitTests.TotalTests);

            // Sortie anticipée si les tests échouent
            if (!context.TestResults.AllPassed)
            {
                _logger.LogWarning("[Cycle {Id}] Tests échoués — rollback immédiat.", context.CycleId);
                context.Status = CycleStatus.Rejected;
                await ExecuteRollbackAsync(context, RollbackReason.TestsFailed, ct);
                return;
            }

            // ── Phase 4 : Évaluation et décision via Claude Code ───────────────
            context.CurrentPhase = CyclePhase.DecisionAndComparison;
            _logger.LogInformation("[Cycle {Id}] Phase 4 : Évaluation", context.CycleId);

            var evaluationPrompt = PromptTemplates.EvaluateChanges(
                topHypothesis,
                context.TestResults,
                baseline);

            var evaluationResult = await _claudeCodeExecutor.ExecuteAsync(
                evaluationPrompt,
                ct: ct);

            if (evaluationResult.Success)
            {
                var evalResponse = parser.ParseEvaluationResponse(evaluationResult.Output);
                if (evalResponse != null)
                {
                    context.EvaluationResult = new EvaluationResult
                    {
                        CycleId = context.CycleId,
                        Decision = evalResponse.Decision,
                        OverallImprovementScore = evalResponse.ImprovementScore,
                        DecisionRationale = evalResponse.Rationale,
                        StatisticalTests = [],
                        ThresholdComparison = new ThresholdComparison
                        {
                            UnitTestsPassed = context.TestResults.UnitTests.AllPassed,
                            RegressionPassed = context.TestResults.Regression.AllPassed,
                            PerformanceImproved = evalResponse.ImprovementScore > 0,
                            StatisticallySignificant = evalResponse.Confidence > 0.8,
                            ActualImprovementPercent = evalResponse.ImprovementScore,
                            RequiredImprovementPercent = 5.0
                        },
                        EvaluatedAt = DateTimeOffset.UtcNow
                    };
                }
            }
            else
            {
                // Fallback: utiliser l'ancien moteur d'évaluation
                context.EvaluationResult = await _evaluationEngine.EvaluateAsync(
                    baseline, context.TestResults, context, ct);
            }

            _metrics.RecordDecision(context.EvaluationResult!.Decision);

            _logger.LogInformation(
                "[Cycle {Id}] Décision : {Decision} (score={Score:F2}%). Rationale : {Rationale}",
                context.CycleId,
                context.EvaluationResult.Decision,
                context.EvaluationResult.OverallImprovementScore,
                context.EvaluationResult.DecisionRationale);

            switch (context.EvaluationResult.Decision)
            {
                case DecisionOutcome.Accept:
                    await HandleAcceptAsync(context, ct);
                    break;

                case DecisionOutcome.Reject:
                    context.Status = CycleStatus.Rejected;
                    await ExecuteRollbackAsync(context, RollbackReason.EvaluationFailed, ct);
                    break;

                case DecisionOutcome.Defer:
                    context.Status = CycleStatus.Deferred;
                    _logger.LogInformation("[Cycle {Id}] Décision différée — branche conservée pour analyse.", context.CycleId);
                    await _versioning.DeleteBranchAsync(context.CycleId.BranchName, ct);
                    break;
            }

            _consecutiveFailures = context.Status == CycleStatus.Succeeded ? 0 : _consecutiveFailures + 1;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[Cycle {Id}] Cycle annulé (arrêt demandé).", context.CycleId);
            context.Status = CycleStatus.Failed;
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Cycle {Id}] Échec inattendu en phase {Phase}.",
                context.CycleId, context.CurrentPhase);

            context.ErrorMessage = ex.Message;
            context.Status = CycleStatus.Failed;
            _consecutiveFailures++;

            try
            {
                await ExecuteRollbackAsync(context, RollbackReason.UnhandledException, ct);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogCritical(rollbackEx,
                    "[Cycle {Id}] Le rollback d'urgence a également échoué !",
                    context.CycleId);

                await _alertManager.SendAlertAsync(
                    AlertSeverity.Critical,
                    "ROLLBACK CRITIQUE ÉCHOUÉ",
                    $"Cycle {context.CycleId} : {rollbackEx.Message}",
                    ct);
            }
        }
        finally
        {
            context.CurrentPhase = context.Status == CycleStatus.Succeeded
                ? CyclePhase.Completed
                : context.RollbackResult != null ? CyclePhase.RolledBack : CyclePhase.Failed;

            context.CompletedAt = DateTimeOffset.UtcNow;

            _metrics.RecordCycleCompleted(context.CycleId, context.Status, context.Duration);

            // Stocker le résumé du cycle
            await _cycleMemory.StoreCycleSummaryAsync(new CycleSummary
            {
                CycleId = context.CycleId,
                Status = context.Status,
                CompletedAt = context.CompletedAt.Value,
                HypothesisSummary = context.Hypotheses.Count > 0 ? context.Hypotheses[0].Rationale[..Math.Min(100, context.Hypotheses[0].Rationale.Length)] : null,
                ImprovementScore = context.EvaluationResult?.OverallImprovementScore,
                Decision = context.EvaluationResult?.Decision.ToString(),
                MutationType = context.AppliedChange?.MutationType.ToString(),
                ModifiedFiles = context.AppliedChange != null ? [context.AppliedChange.FilePath] : [],
                Duration = context.Duration,
                UserIntent = context.UserIntent?.OriginalIntent
            }, ct);

            await _journal.EndCycleAsync(context, ct);

            _logger.LogInformation(
                "=== Fin du cycle #{Num} [{Id}] — {Status} en {Ms}ms ===",
                _cycleCount, context.CycleId, context.Status, (int)context.Duration.TotalMilliseconds);

            // Alerte si trop d'échecs consécutifs
            if (_consecutiveFailures >= 3)
            {
                await _alertManager.SendAlertAsync(
                    AlertSeverity.Warning,
                    "Échecs consécutifs détectés",
                    $"{_consecutiveFailures} cycles consécutifs ont échoué. Vérification manuelle recommandée.",
                    ct);
            }
        }
    }

    private async Task HandleAcceptAsync(CycleContext context, CancellationToken ct)
    {
        _logger.LogInformation("[Cycle {Id}] Amélioration acceptée — création de la Pull Request.", context.CycleId);

        var prUrl = await _versioning.CreatePullRequestAsync(context, ct);
        context.AppliedChange = context.AppliedChange! with { PullRequestUrl = prUrl };

        await _versioning.MergePullRequestAsync(prUrl, ct);
        await _baselineStore.StoreBaselineAsync(context.TestResults!, ct);

        context.Status = CycleStatus.Succeeded;
        context.CurrentPhase = CyclePhase.Completed;

        _logger.LogInformation("[Cycle {Id}] PR mergée : {Url}", context.CycleId, prUrl);
    }

    private async Task ExecuteRollbackAsync(CycleContext context, RollbackReason reason, CancellationToken ct)
    {
        _logger.LogWarning("[Cycle {Id}] Rollback déclenché. Raison : {Reason}", context.CycleId, reason);
        _metrics.RecordRollback(reason);

        context.RollbackResult = await _rollback.RollbackAsync(context, reason, ct);
        context.CurrentPhase = CyclePhase.RolledBack;

        // Supprimer la branche distante si elle existe
        if (context.AppliedChange != null)
        {
            try
            {
                await _versioning.DeleteBranchAsync(context.CycleId.BranchName, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Cycle {Id}] Impossible de supprimer la branche distante.", context.CycleId);
            }
        }
    }
}