using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoLoop.Core.Models;
using AutoLoop.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoLoop.Logging;

/// <summary>
/// Journal d'audit immuable avec chaîne de hachage SHA-256.
/// Chaque entrée contient le hash de la précédente, rendant la falsification détectable.
/// </summary>
public interface IAuditTrail
{
    Task RecordAsync(AuditEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<AuditEntry>> QueryAsync(
        string? cycleId = null,
        string? eventType = null,
        DateTimeOffset? since = null,
        CancellationToken ct = default);
    Task<bool> VerifyIntegrityAsync(CancellationToken ct = default);
}

public sealed record AuditEntry
{
    public required Guid Id { get; init; }
    public required string CycleId { get; init; }
    public required string EventType { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string Actor { get; init; }
    public required Dictionary<string, object> Payload { get; init; }
    public string? PreviousHash { get; init; }
    public string? Hash { get; init; }
}

public sealed class JsonlAuditTrail : IAuditTrail
{
    private readonly string _filePath;
    private readonly ILogger<JsonlAuditTrail> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private string? _lastHash;

    public JsonlAuditTrail(IOptions<StorageOptions> options, ILogger<JsonlAuditTrail> logger)
    {
        _filePath = options.Value.AuditTrailPath;
        _logger = logger;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        _lastHash = LoadLastHash();
    }

    public async Task RecordAsync(AuditEntry entry, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            var entryWithHash = entry with
            {
                PreviousHash = _lastHash,
                Hash = null // calculé après
            };

            var json = JsonSerializer.Serialize(entryWithHash);
            var hash = ComputeHash(_lastHash + json);
            var finalEntry = entryWithHash with { Hash = hash };

            var line = JsonSerializer.Serialize(finalEntry) + Environment.NewLine;
            await File.AppendAllTextAsync(_filePath, line, ct);
            _lastHash = hash;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<IReadOnlyList<AuditEntry>> QueryAsync(
        string? cycleId = null,
        string? eventType = null,
        DateTimeOffset? since = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(_filePath)) return [];

        var lines = await File.ReadAllLinesAsync(_filePath, ct);
        var results = new List<AuditEntry>();

        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<AuditEntry>(line);
                if (entry is null) continue;

                if (cycleId != null && entry.CycleId != cycleId) continue;
                if (eventType != null && entry.EventType != eventType) continue;
                if (since.HasValue && entry.Timestamp < since.Value) continue;

                results.Add(entry);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Ligne d'audit corrompue ignorée.");
            }
        }

        return results;
    }

    public async Task<bool> VerifyIntegrityAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath)) return true;

        var lines = await File.ReadAllLinesAsync(_filePath, ct);
        string? previousHash = null;

        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<AuditEntry>(line)!;
                var entryWithoutHash = entry with { Hash = null };
                var json = JsonSerializer.Serialize(entryWithoutHash);
                var expectedHash = ComputeHash(previousHash + json);

                if (entry.Hash != expectedHash)
                {
                    _logger.LogError("Intégrité de l'audit violée à l'entrée {Id}.", entry.Id);
                    return false;
                }

                if (entry.PreviousHash != previousHash)
                {
                    _logger.LogError("Chaîne de hash rompue à l'entrée {Id}.", entry.Id);
                    return false;
                }

                previousHash = entry.Hash;
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    private string? LoadLastHash()
    {
        if (!File.Exists(_filePath)) return null;

        var lines = File.ReadAllLines(_filePath);
        var lastLine = lines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l));
        if (lastLine is null) return null;

        try
        {
            var entry = JsonSerializer.Deserialize<AuditEntry>(lastLine);
            return entry?.Hash;
        }
        catch { return null; }
    }

    private static string ComputeHash(string? input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
