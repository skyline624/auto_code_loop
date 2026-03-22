using AutoLoop.Core.Interfaces;
using AutoLoop.Core.Models;

namespace AutoLoop.Core.Prompts;

/// <summary>
/// Implémentation statique de IPromptProvider : délègue vers PromptTemplates.
/// Utilisée quand UseAdaptivePrompts = false ou comme base pour AdaptivePromptProvider.
/// </summary>
public sealed class StaticPromptProvider : IPromptProvider
{
    public Task<string> GetHypothesisPromptAsync(
        UserIntent intent,
        ProjectInfo project,
        IReadOnlyList<CycleSummary> recentCycles,
        MetricsSnapshot? metrics,
        CancellationToken ct = default)
        => Task.FromResult(PromptTemplates.GenerateHypotheses(intent, project, recentCycles, metrics));

    public Task<string> GetMutationPromptAsync(
        Hypothesis hypothesis,
        ProjectInfo project,
        CancellationToken ct = default)
        => Task.FromResult(PromptTemplates.ApplyMutation(hypothesis, project));

    public Task<string> GetTestGenerationPromptAsync(
        ChangeRecord appliedChange,
        ProjectInfo project,
        string hypothesisRationale,
        CancellationToken ct = default)
        => Task.FromResult(PromptTemplates.GenerateTargetedTests(appliedChange, project, hypothesisRationale));

    public Task<string> GetEvaluationPromptAsync(
        Hypothesis hypothesis,
        TestSuite testResults,
        TestSuite? baseline,
        CancellationToken ct = default)
        => Task.FromResult(PromptTemplates.EvaluateChanges(hypothesis, testResults, baseline));
}
