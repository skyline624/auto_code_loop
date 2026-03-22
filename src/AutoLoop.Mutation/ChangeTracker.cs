using System.Text.Json;
using AutoLoop.Core.Models;
using AutoLoop.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoLoop.Mutation;

/// <summary>
/// Stocke chaque ChangeRecord dans un fichier JSONL pour traçabilité complète.
/// Permet de retrouver tous les changements d'un cycle donné.
/// </summary>
public interface IChangeTracker
{
    Task RecordAsync(ChangeRecord change, CancellationToken ct = default);
    Task<ChangeRecord?> GetAsync(Guid changeId, CancellationToken ct = default);
    Task<IReadOnlyList<ChangeRecord>> GetByCycleAsync(CycleId cycleId, CancellationToken ct = default);
}

public sealed class JsonlChangeTracker : IChangeTracker
{
    private readonly string _filePath;
    private readonly ILogger<JsonlChangeTracker> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public JsonlChangeTracker(IOptions<StorageOptions> options, ILogger<JsonlChangeTracker> logger)
    {
        _filePath = options.Value.ChangeTrackerPath;
        _logger = logger;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
    }

    public async Task RecordAsync(ChangeRecord change, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            var line = JsonSerializer.Serialize(change) + Environment.NewLine;
            await File.AppendAllTextAsync(_filePath, line, ct);
            _logger.LogDebug("ChangeRecord {Id} enregistré.", change.Id);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<ChangeRecord?> GetAsync(Guid changeId, CancellationToken ct = default)
    {
        if (!File.Exists(_filePath)) return null;

        var lines = await File.ReadAllLinesAsync(_filePath, ct);
        foreach (var line in lines.Reverse()) // Les plus récents en premier
        {
            try
            {
                var record = JsonSerializer.Deserialize<ChangeRecord>(line);
                if (record?.Id == changeId) return record;
            }
            catch { }
        }

        return null;
    }

    public async Task<IReadOnlyList<ChangeRecord>> GetByCycleAsync(
        CycleId cycleId, CancellationToken ct = default)
    {
        if (!File.Exists(_filePath)) return [];

        var lines = await File.ReadAllLinesAsync(_filePath, ct);
        var results = new List<ChangeRecord>();
        var cycleIdStr = cycleId.ToString();

        foreach (var line in lines)
        {
            try
            {
                var record = JsonSerializer.Deserialize<ChangeRecord>(line);
                if (record?.CycleId.ToString() == cycleIdStr)
                    results.Add(record);
            }
            catch { }
        }

        return results;
    }
}
