using AutoLoop.Core.Options;
using AutoLoop.Logging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoLoop.Tests.Logging;

public sealed class AuditTrailTests : IDisposable
{
    private readonly string _tempFile;
    private readonly JsonlAuditTrail _sut;

    public AuditTrailTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"audit-test-{Guid.NewGuid():N}.jsonl");

        var options = Options.Create(new StorageOptions
        {
            AuditTrailPath = _tempFile
        });

        _sut = new JsonlAuditTrail(options, NullLogger<JsonlAuditTrail>.Instance);
    }

    [Fact]
    public async Task RecordAsync_CreatesFile()
    {
        var entry = CreateEntry("CycleStarted");

        await _sut.RecordAsync(entry);

        File.Exists(_tempFile).Should().BeTrue();
    }

    [Fact]
    public async Task RecordAsync_MultipleEntries_AllStoredInOrder()
    {
        await _sut.RecordAsync(CreateEntry("Event1"));
        await _sut.RecordAsync(CreateEntry("Event2"));
        await _sut.RecordAsync(CreateEntry("Event3"));

        var results = await _sut.QueryAsync();

        results.Should().HaveCount(3);
        results[0].EventType.Should().Be("Event1");
        results[2].EventType.Should().Be("Event3");
    }

    [Fact]
    public async Task VerifyIntegrityAsync_UnmodifiedFile_ReturnsTrue()
    {
        await _sut.RecordAsync(CreateEntry("E1"));
        await _sut.RecordAsync(CreateEntry("E2"));

        var isValid = await _sut.VerifyIntegrityAsync();

        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task QueryAsync_FilterByCycleId_ReturnsMatchingOnly()
    {
        var cycleId = Guid.NewGuid().ToString("N");
        await _sut.RecordAsync(CreateEntry("E1", cycleId));
        await _sut.RecordAsync(CreateEntry("E2", "other-cycle"));
        await _sut.RecordAsync(CreateEntry("E3", cycleId));

        var results = await _sut.QueryAsync(cycleId: cycleId);

        results.Should().HaveCount(2);
        results.All(e => e.CycleId == cycleId).Should().BeTrue();
    }

    private static AuditEntry CreateEntry(string eventType, string? cycleId = null) => new()
    {
        Id = Guid.NewGuid(),
        CycleId = cycleId ?? Guid.NewGuid().ToString("N"),
        EventType = eventType,
        Timestamp = DateTimeOffset.UtcNow,
        Actor = "System",
        Payload = new Dictionary<string, object> { ["test"] = true }
    };

    public void Dispose()
    {
        try { if (File.Exists(_tempFile)) File.Delete(_tempFile); } catch { }
    }
}
