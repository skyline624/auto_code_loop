using AutoLoop.Core.Models;
using FluentAssertions;
using Xunit;

namespace AutoLoop.Tests.Core;

public sealed class CycleIdTests
{
    [Fact]
    public void New_CreatesUniqueIds()
    {
        var id1 = CycleId.New();
        var id2 = CycleId.New();

        id1.Value.Should().NotBe(id2.Value);
    }

    [Fact]
    public void BranchName_FollowsExpectedFormat()
    {
        var id = new CycleId(new Guid("12345678-1234-1234-1234-123456789012"));

        id.BranchName.Should().StartWith("auto-loop/cycle-");
        // Le GUID est formaté sans tirets via :N — seule la partie GUID ne doit pas contenir de tirets
        var guidPart = id.BranchName["auto-loop/cycle-".Length..];
        guidPart.Should().NotContain("-");
        guidPart.Should().HaveLength(32);
    }

    [Fact]
    public void ToString_ReturnsGuidWithoutDashes()
    {
        var guid = Guid.NewGuid();
        var id = new CycleId(guid);

        id.ToString().Should().Be(guid.ToString("N"));
    }
}
