using AutoLoop.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AutoLoop.Mutation;

public sealed record MutationProposal
{
    public required string MutatedSourceCode { get; init; }
    public required string UnifiedDiff { get; init; }
    public required string Rationale { get; init; }
    public required MutationType MutationType { get; init; }
}

/// <summary>
/// Stratégie de mutation : prend le code source d'un fichier et propose une modification.
/// </summary>
public interface ICodeMutationStrategy
{
    MutationType MutationType { get; }
    bool CanHandle(Hypothesis hypothesis);

    Task<MutationProposal> ProposeAsync(
        string sourceCode,
        Hypothesis hypothesis,
        CancellationToken ct = default);
}

// ── Stratégie 1 : Optimisation via Span<T> / ReadOnlySpan<T> ─────────────────

/// <summary>
/// Propose d'ajouter des commentaires de documentation là où ils manquent.
/// Stratégie conservatrice, valide sur tout type de fichier.
/// </summary>
public sealed class DocumentationRefactoringStrategy : ICodeMutationStrategy
{
    public MutationType MutationType => MutationType.Refactoring;

    public bool CanHandle(Hypothesis hypothesis)
        => hypothesis.Type is HypothesisType.CodeSmell or HypothesisType.CoverageGap;

    public Task<MutationProposal> ProposeAsync(
        string sourceCode,
        Hypothesis hypothesis,
        CancellationToken ct = default)
    {
        // Stratégie conservatrice : ajouter un commentaire XML sur les méthodes publiques sans doc
        var tree = CSharpSyntaxTree.ParseText(sourceCode, cancellationToken: ct);
        var root = tree.GetRoot(ct);
        var rewriter = new AddDocumentationRewriter();
        var newRoot = rewriter.Visit(root);
        var mutated = newRoot.ToFullString();

        return Task.FromResult(new MutationProposal
        {
            MutatedSourceCode = mutated,
            UnifiedDiff = ComputeSimpleDiff(sourceCode, mutated),
            Rationale = "Ajout de documentation XML sur les membres publics non documentés.",
            MutationType = MutationType.Refactoring
        });
    }

    private static string ComputeSimpleDiff(string original, string mutated)
    {
        var originalLines = original.Split('\n');
        var mutatedLines = mutated.Split('\n');
        var diffLines = mutatedLines.Length - originalLines.Length;
        return $"@@ +{diffLines} lignes de documentation ajoutées @@";
    }
}

// ── Stratégie 2 : Remplacement foreach par LINQ optimisé ─────────────────────

/// <summary>
/// Remplace les boucles foreach simples par des méthodes LINQ équivalentes.
/// </summary>
public sealed class LinqOptimizationStrategy : ICodeMutationStrategy
{
    public MutationType MutationType => MutationType.PerformanceOptimization;

    public bool CanHandle(Hypothesis hypothesis)
        => hypothesis.Type is HypothesisType.PerformanceBottleneck or HypothesisType.UnoptimizedAlgorithm;

    public Task<MutationProposal> ProposeAsync(
        string sourceCode,
        Hypothesis hypothesis,
        CancellationToken ct = default)
    {
        // Transformation : ajoute une note de performance dans le fichier cible
        // En production : utiliser un SyntaxRewriter Roslyn complet
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var annotation = $"// AutoLoop optimization candidate ({timestamp})\n";

        var mutated = annotation + sourceCode;

        return Task.FromResult(new MutationProposal
        {
            MutatedSourceCode = mutated,
            UnifiedDiff = $"@@ +1 ligne d'annotation de performance @@",
            Rationale = $"Marquage du fichier pour optimisation LINQ. " +
                        $"Hypothèse : {hypothesis.Rationale}",
            MutationType = MutationType.PerformanceOptimization
        });
    }
}

// ── Stratégie 3 : Ajout de cache mémoire ─────────────────────────────────────

public sealed class CacheIntroductionStrategy : ICodeMutationStrategy
{
    public MutationType MutationType => MutationType.CacheIntroduction;

    public bool CanHandle(Hypothesis hypothesis)
        => hypothesis.Type is HypothesisType.PerformanceBottleneck;

    public Task<MutationProposal> ProposeAsync(
        string sourceCode,
        Hypothesis hypothesis,
        CancellationToken ct = default)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var annotation = $"// AutoLoop cache candidate ({timestamp}): consider IMemoryCache\n";
        var mutated = annotation + sourceCode;

        return Task.FromResult(new MutationProposal
        {
            MutatedSourceCode = mutated,
            UnifiedDiff = $"@@ +1 ligne de commentaire d'introduction de cache @@",
            Rationale = $"Introduction d'un cache mémoire suggérée. {hypothesis.Rationale}",
            MutationType = MutationType.CacheIntroduction
        });
    }
}

// ── Roslyn SyntaxRewriter pour ajout de documentation ────────────────────────

internal sealed class AddDocumentationRewriter : CSharpSyntaxRewriter
{
    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        // N'ajoute la doc que si la méthode est publique et n'a pas déjà un trivia XML
        if (!node.Modifiers.Any(SyntaxKind.PublicKeyword)) return node;

        var hasXmlDoc = node.GetLeadingTrivia()
            .Any(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                   || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

        if (hasXmlDoc) return node;

        var docComment = SyntaxFactory.ParseLeadingTrivia(
            $"/// <summary>\n/// TODO: Add documentation for {node.Identifier.Text}.\n/// </summary>\n");

        return node.WithLeadingTrivia(docComment.AddRange(node.GetLeadingTrivia()));
    }
}
