using AutoLoop.Core.Exceptions;
using AutoLoop.Core.Interfaces;
using AutoLoop.Core.Models;
using Microsoft.Extensions.Logging;

namespace AutoLoop.Mutation;

/// <summary>
/// Moteur de mutation. Sélectionne la stratégie appropriée, valide la mutation via Roslyn,
/// applique le changement sur disque et l'enregistre dans le ChangeTracker.
/// </summary>
public sealed class MutationEngine : IMutationEngine
{
    private readonly IReadOnlyList<ICodeMutationStrategy> _strategies;
    private readonly ICodeValidator _validator;
    private readonly IChangeTracker _changeTracker;
    private readonly ILogger<MutationEngine> _logger;

    public MutationEngine(
        IEnumerable<ICodeMutationStrategy> strategies,
        ICodeValidator validator,
        IChangeTracker changeTracker,
        ILogger<MutationEngine> logger)
    {
        _strategies = strategies.ToList();
        _validator = validator;
        _changeTracker = changeTracker;
        _logger = logger;
    }

    public async Task<ChangeRecord> ApplyMutationAsync(
        Hypothesis hypothesis,
        CycleContext context,
        CancellationToken ct = default)
    {
        var strategy = _strategies.FirstOrDefault(s => s.CanHandle(hypothesis))
            ?? throw new InvalidOperationException(
                $"Aucune stratégie disponible pour le type d'hypothèse {hypothesis.Type}.");

        _logger.LogDebug(
            "Stratégie sélectionnée : {Strategy} pour {File}",
            strategy.GetType().Name, hypothesis.TargetFile);

        // Vérifier que le fichier cible existe (sinon prendre un fichier par défaut)
        var targetFile = ResolveTargetFile(hypothesis);

        var originalContent = await File.ReadAllTextAsync(targetFile, ct);

        // Proposer la mutation
        var proposal = await strategy.ProposeAsync(originalContent, hypothesis, ct);

        // Valider que le code muté compile
        var validation = await _validator.ValidateAsync(proposal.MutatedSourceCode, ct);
        if (!validation.IsValid)
        {
            _logger.LogWarning(
                "Validation Roslyn échouée pour l'hypothèse {Id}: {Errors}",
                hypothesis.Id, string.Join("; ", validation.Errors));
            throw new MutationValidationException(hypothesis, validation.Errors);
        }

        // Créer le ChangeRecord avec le contenu original (pour rollback in-memory)
        var change = new ChangeRecord
        {
            Id = Guid.NewGuid(),
            CycleId = context.CycleId,
            HypothesisId = hypothesis.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            FilePath = targetFile,
            OriginalContent = originalContent,
            MutatedContent = proposal.MutatedSourceCode,
            UnifiedDiff = proposal.UnifiedDiff,
            MutationType = proposal.MutationType,
            Rationale = proposal.Rationale
        };

        // Appliquer physiquement sur disque
        await File.WriteAllTextAsync(targetFile, proposal.MutatedSourceCode, ct);
        _logger.LogInformation(
            "Mutation appliquée : {File}, Type={Type}, ChangeId={Id}",
            targetFile, proposal.MutationType, change.Id);

        // Enregistrer pour traçabilité
        await _changeTracker.RecordAsync(change, ct);

        return change;
    }

    public async Task RevertMutationAsync(ChangeRecord change, CancellationToken ct = default)
    {
        await File.WriteAllTextAsync(change.FilePath, change.OriginalContent, ct);
        _logger.LogInformation("Mutation revertée pour le fichier {File}.", change.FilePath);
    }

    private static string ResolveTargetFile(Hypothesis hypothesis)
    {
        if (File.Exists(hypothesis.TargetFile))
            return hypothesis.TargetFile;

        // Fichier de démonstration créé si le cible n'existe pas encore
        const string demoFile = "./src/AutoLoop.CLI/SampleTarget.cs";
        if (!File.Exists(demoFile))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(demoFile)!);
            File.WriteAllText(demoFile,
                """
                namespace AutoLoop.CLI;

                // Fichier cible de démonstration pour le framework d'auto-amélioration.
                public static class SampleTarget
                {
                    public static int SumArray(int[] numbers)
                    {
                        int sum = 0;
                        foreach (var n in numbers) sum += n;
                        return sum;
                    }

                    public static List<int> FilterPositive(List<int> numbers)
                    {
                        var result = new List<int>();
                        foreach (var n in numbers) if (n > 0) result.Add(n);
                        return result;
                    }
                }
                """);
        }

        return demoFile;
    }
}
