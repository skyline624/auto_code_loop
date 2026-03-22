using System.Text.Json;
using System.Text.Json.Serialization;
using AutoLoop.Core.Interfaces;
using AutoLoop.Core.Models;
using AutoLoop.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoLoop.Logging;

/// <summary>
/// Journal JSONL append-only d'un cycle complet.
/// Chaque cycle écrit une entrée au début et une à la fin.
/// Utilisé par HypothesisEngine pour lire l'historique des cycles.
/// </summary>
public sealed class JsonlCycleJournal : ICycleJournal
{
    private readonly string _journalDir;
    private readonly ILogger<JsonlCycleJournal> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public JsonlCycleJournal(IOptions<StorageOptions> options, ILogger<JsonlCycleJournal> logger)
    {
        _journalDir = options.Value.JournalsPath;
        _logger = logger;
        Directory.CreateDirectory(_journalDir);
    }

    public async Task BeginCycleAsync(CycleContext context, CancellationToken ct = default)
    {
        var entry = new
        {
            Event = "CycleStarted",
            CycleId = context.CycleId.ToString(),
            context.StartedAt
        };

        await AppendEntryAsync(entry, ct);
    }

    public async Task EndCycleAsync(CycleContext context, CancellationToken ct = default)
    {
        var entry = new
        {
            Event = "CycleCompleted",
            CycleId = context.CycleId.ToString(),
            context.StartedAt,
            context.CompletedAt,
            Duration = context.Duration.TotalSeconds,
            Status = context.Status.ToString(),
            Phase = context.CurrentPhase.ToString(),
            HypothesesCount = context.Hypotheses.Count,
            TopHypothesisType = context.Hypotheses.Count > 0 ? context.Hypotheses[0].Type.ToString() : null,
            ChangeId = context.AppliedChange?.Id,
            MutationType = context.AppliedChange?.MutationType.ToString(),
            CommitSha = context.AppliedChange?.CommitSha,
            PullRequestUrl = context.AppliedChange?.PullRequestUrl,
            UnitTestsPassed = context.TestResults?.UnitTests.AllPassed,
            RegressionPassed = context.TestResults?.Regression.AllPassed,
            Decision = context.EvaluationResult?.Decision.ToString(),
            ImprovementScore = context.EvaluationResult?.OverallImprovementScore,
            RollbackExecuted = context.RollbackResult != null,
            RollbackSucceeded = context.RollbackResult?.Succeeded,
            context.ErrorMessage
        };

        await AppendEntryAsync(entry, ct);
    }

    public async Task<IReadOnlyList<object>> GetRecentCyclesAsync(int limit = 20, CancellationToken ct = default)
    {
        var path = GetJournalPath();
        if (!File.Exists(path)) return [];

        var lines = await File.ReadAllLinesAsync(path, ct);
        var completed = lines
            .Where(l => l.Contains("\"CycleCompleted\""))
            .TakeLast(limit)
            .Select(l =>
            {
                try { return (object?)JsonSerializer.Deserialize<Dictionary<string, object>>(l); }
                catch { return null; }
            })
            .Where(e => e != null)
            .Cast<object>()
            .ToList();

        return completed;
    }

    private async Task AppendEntryAsync<T>(T entry, CancellationToken ct) where T : class
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
            await File.AppendAllTextAsync(GetJournalPath(), line, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible d'écrire dans le journal JSONL.");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private string GetJournalPath()
    {
        var date = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        return Path.Combine(_journalDir, $"cycles-{date}.jsonl");
    }
}
