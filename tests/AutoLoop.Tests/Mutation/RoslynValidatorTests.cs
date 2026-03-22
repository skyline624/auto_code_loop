using AutoLoop.Mutation;
using FluentAssertions;
using Xunit;

namespace AutoLoop.Tests.Mutation;

public sealed class RoslynValidatorTests
{
    private readonly RoslynCodeValidator _sut = new();

    [Fact]
    public async Task ValidateAsync_ValidCode_ReturnsIsValidTrue()
    {
        const string validCode = """
            namespace Test;

            public static class Calculator
            {
                public static int Add(int a, int b) => a + b;
            }
            """;

        var result = await _sut.ValidateAsync(validCode);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_InvalidCode_ReturnsIsValidFalse()
    {
        const string invalidCode = """
            namespace Test;

            public class Broken
            {
                public void Method( // parenthèse non fermée
            }
            """;

        var result = await _sut.ValidateAsync(invalidCode);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_EmptyCode_ReturnsIsValidTrue()
    {
        var result = await _sut.ValidateAsync(string.Empty);

        // Un fichier vide est syntaxiquement valide en C#
        result.Errors.Should().BeEmpty();
    }
}
