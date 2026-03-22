using System.Text.Json;
using AutoLoop.Core.Interfaces;
using AutoLoop.Core.Models;
using AutoLoop.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoLoop.ClaudeCode;

/// <summary>
/// Implémentation de la mémoire persistante entre cycles.
/// Stocke les résumés de cycles dans un fichier JSONL append-only.
/// </summary>
public sealed class CycleMemory : ICycleMemory
{
    private readonly ILogger<CycleMemory> _logger;
    private readonly string _memoryPath;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CycleMemory(
        IOptions<StorageOptions> options,
        ILogger<CycleMemory> logger)
    {
        _memoryPath = options.Value.MemoryPath;
        _logger = logger;
        Directory.CreateDirectory(_memoryPath);
    }

    public async Task<IReadOnlyList<CycleSummary>> GetRecentCyclesAsync(int limit = 10, CancellationToken ct = default)
    {
        var filePath = GetCycleMemoryPath();

        if (!File.Exists(filePath))
            return [];

        await _semaphore.WaitAsync(ct);
        try
        {
            var lines = await File.ReadAllLinesAsync(filePath, ct);

            return lines
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(ParseCycleSummary)
                .Where(s => s != null)
                .Cast<CycleSummary>()
                .OrderByDescending(s => s.CompletedAt)
                .Take(limit)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de charger les cycles récents");
            return [];
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task StoreCycleSummaryAsync(CycleSummary summary, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            var filePath = GetCycleMemoryPath();
            var json = JsonSerializer.Serialize(summary, JsonOptions);
            await File.AppendAllTextAsync(filePath, json + Environment.NewLine, ct);
            _logger.LogDebug("Résumé de cycle stocké: {CycleId}", summary.CycleId);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static CycleSummary? ParseCycleSummary(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<CycleSummary>(line, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private string GetCycleMemoryPath()
    {
        var date = DateTimeOffset.UtcNow.ToString("yyyy-MM");
        return Path.Combine(_memoryPath, $"cycles-{date}.jsonl");
    }
}