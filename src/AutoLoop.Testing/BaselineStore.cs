using System.Text.Json;
using System.Text.Json.Serialization;
using AutoLoop.Core.Interfaces;
using AutoLoop.Core.Models;
using AutoLoop.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoLoop.Testing;

/// <summary>
/// Stocke et charge la baseline de tests (dernière version acceptée) en JSON.
/// </summary>
public sealed class JsonFileBaselineStore : IBaselineStore
{
    private readonly string _filePath;
    private readonly ILogger<JsonFileBaselineStore> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonFileBaselineStore(IOptions<StorageOptions> options, ILogger<JsonFileBaselineStore> logger)
    {
        _filePath = options.Value.BaselinePath;
        _logger = logger;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
    }

    public async Task<TestSuite?> GetLatestBaselineAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct);
            return JsonSerializer.Deserialize<TestSuite>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de charger la baseline depuis {Path}.", _filePath);
            return null;
        }
    }

    public async Task StoreBaselineAsync(TestSuite suite, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            var json = JsonSerializer.Serialize(suite, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json, ct);
            _logger.LogInformation("Nouvelle baseline stockée (cycle {CycleId}).", suite.CycleId);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
