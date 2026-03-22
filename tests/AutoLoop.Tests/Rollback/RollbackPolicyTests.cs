using AutoLoop.Core.Models;
using AutoLoop.Rollback;
using FluentAssertions;
using Xunit;

namespace AutoLoop.Tests.Rollback;

public sealed class RollbackPolicyTests
{
    private readonly DefaultRollbackPolicy _sut = new();

    [Fact]
    public void SelectStrategy_WithAppliedChange_NoCommit_ReturnsInMemoryRestore()
    {
        var ctx = new CycleContext
        {
            AppliedChange = new ChangeRecord
            {
                Id = Guid.NewGuid(),
                CycleId = CycleId.New(),
                HypothesisId = "h1",
                CreatedAt = DateTimeOffset.UtcNow,
                FilePath = "test.cs",
                OriginalContent = "// original",
                MutatedContent = "// mutated",
                UnifiedDiff = "@@ test @@",
                MutationType = MutationType.Refactoring,
                Rationale = "Test",
                CommitSha = null
            }
        };

        var strategy = _sut.SelectStrategy(ctx, RollbackReason.EvaluationFailed);

        strategy.Should().Be(RollbackStrategy.InMemoryRestore);
    }

    [Fact]
    public void SelectStrategy_WithCommitSha_ReturnsGitRevert()
    {
        var ctx = new CycleContext
        {
            AppliedChange = new ChangeRecord
            {
                Id = Guid.NewGuid(),
                CycleId = CycleId.New(),
                HypothesisId = "h1",
                CreatedAt = DateTimeOffset.UtcNow,
                FilePath = "test.cs",
                OriginalContent = "// original",
                MutatedContent = "// mutated",
                UnifiedDiff = "@@ test @@",
                MutationType = MutationType.Refactoring,
                Rationale = "Test",
                CommitSha = "abc123def456"
            }
        };

        // HealthCheckFailed → pas InMemoryRestore, cherche le suivant
        var strategy = _sut.SelectStrategy(ctx, RollbackReason.HealthCheckFailed);

        strategy.Should().Be(RollbackStrategy.GitRevert);
    }

    [Fact]
    public void SelectStrategy_NoAppliedChange_ReturnsGitCheckout()
    {
        var ctx = new CycleContext { AppliedChange = null };

        var strategy = _sut.SelectStrategy(ctx, RollbackReason.UnhandledException);

        strategy.Should().Be(RollbackStrategy.GitCheckout);
    }
}
