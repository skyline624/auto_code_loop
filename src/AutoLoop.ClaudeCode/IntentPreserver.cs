using System.Text;
using System.Text.Json;
using AutoLoop.Core.Interfaces;
using AutoLoop.Core.Models;
using AutoLoop.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoLoop.ClaudeCode;

/// <summary>
/// Implémentation de la préservation d'intention utilisateur.
/// Stocke l'intention dans un fichier JSON pour persistance cross-cycles.
/// </summary>
public sealed class IntentPreserver : IIntentPreserver
{
    private readonly ILogger<IntentPreserver> _logger;
    private readonly string _memoryPath;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public IntentPreserver(
        IOptions<StorageOptions> options,
        ILogger<IntentPreserver> logger)
    {
        _memoryPath = options.Value.MemoryPath;
        _logger = logger;
        Directory.CreateDirectory(_memoryPath);
    }

    public async Task StoreIntentAsync(CycleId cycleId, UserIntent intent, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            var filePath = GetIntentFilePath(cycleId);
            var json = JsonSerializer.Serialize(intent, JsonOptions);
            await File.WriteAllTextAsync(filePath, json, ct);
            _logger.LogDebug("Intention stockée pour le cycle {CycleId}", cycleId);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<UserIntent?> GetIntentAsync(CycleId cycleId, CancellationToken ct = default)
    {
        var filePath = GetIntentFilePath(cycleId);
        if (!File.Exists(filePath))
            return null;

        await _semaphore.WaitAsync(ct);
        try
        {
            var json = await File.ReadAllTextAsync(filePath, ct);
            return JsonSerializer.Deserialize<UserIntent>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de charger l'intention pour le cycle {CycleId}", cycleId);
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<string> GenerateContextPromptAsync(CycleId cycleId, CancellationToken ct = default)
    {
        var intent = await GetIntentAsync(cycleId, ct);
        if (intent == null)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("**Context from Previous Cycles**");
        sb.AppendLine();
        sb.AppendLine($"Original Intent: {intent.OriginalIntent}");
        sb.AppendLine($"Expanded Intent: {intent.ExpandedIntent}");
        sb.AppendLine();

        if (intent.TargetAreas.Count > 0)
        {
            sb.AppendLine("Target Areas:");
            foreach (var area in intent.TargetAreas)
            {
                sb.AppendLine($"- {area}");
            }
            sb.AppendLine();
        }

        if (intent.Constraints.Count > 0)
        {
            sb.AppendLine("Constraints:");
            foreach (var constraint in intent.Constraints)
            {
                sb.AppendLine($"- {constraint}");
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(intent.ExpectedThreshold))
        {
            sb.AppendLine($"Expected Threshold: {intent.ExpectedThreshold}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private string GetIntentFilePath(CycleId cycleId)
    {
        return Path.Combine(_memoryPath, $"intent-{cycleId.Value:N}.json");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}