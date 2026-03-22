using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AutoLoop.Mutation;

public sealed record CodeValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// Valide que le code muté compile correctement via Roslyn (CSharpCompilation.Emit).
/// </summary>
public interface ICodeValidator
{
    Task<CodeValidationResult> ValidateAsync(string sourceCode, CancellationToken ct = default);
}

public sealed class RoslynCodeValidator : ICodeValidator
{
    public Task<CodeValidationResult> ValidateAsync(string sourceCode, CancellationToken ct = default)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, cancellationToken: ct);

        var references = GetStandardReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName: "AutoLoopValidation",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms, cancellationToken: ct);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();

        var warnings = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Warning)
            .Select(d => d.ToString())
            .ToList();

        return Task.FromResult(new CodeValidationResult
        {
            IsValid = result.Success,
            Errors = errors,
            Warnings = warnings
        });
    }

    private static IReadOnlyList<MetadataReference> GetStandardReferences()
    {
        var dotnetDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        var paths = new[]
        {
            typeof(object).Assembly.Location,
            typeof(Console).Assembly.Location,
            typeof(System.Linq.Enumerable).Assembly.Location,
            typeof(System.Collections.Generic.List<>).Assembly.Location,
            Path.Combine(dotnetDir, "System.Runtime.dll"),
            Path.Combine(dotnetDir, "System.Collections.dll"),
            Path.Combine(dotnetDir, "netstandard.dll"),
        };

        return paths
            .Where(File.Exists)
            .Distinct()
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
    }
}
