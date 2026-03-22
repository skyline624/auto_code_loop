using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoLoop.ClaudeCode.Options;
using AutoLoop.Core.Interfaces;
using AutoLoop.Core.Models;
using AutoLoop.Core.Options;
using AutoLoop.Core.Prompts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoLoop.ClaudeCode;

/// <summary>
/// Fournisseur de prompts adaptatifs : utilise Claude (mode --print) pour analyser
/// le projet une fois et générer des instructions complémentaires spécifiques.
/// Les instructions sont mises en cache dans storage/prompts/{projectHash}.json.
/// </summary>
public sealed class AdaptivePromptProvider : IPromptProvider
{
    private readonly IClaudeCodeExecutor _executor;
    private readonly StaticPromptProvider _static;
    private readonly ClaudeCodeOptions _claudeOptions;
    private readonly StorageOptions _storageOptions;
    private readonly ILogger<AdaptivePromptProvider> _logger;

    // Cache en mémoire pour éviter plusieurs appels dans le même processus
    private AdaptivePromptCache? _cache;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public AdaptivePromptProvider(
        IClaudeCodeExecutor executor,
        StaticPromptProvider staticProvider,
        IOptions<ClaudeCodeOptions> claudeOptions,
        IOptions<StorageOptions> storageOptions,
        ILogger<AdaptivePromptProvider> logger)
    {
        _executor = executor;
        _static = staticProvider;
        _claudeOptions = claudeOptions.Value;
        _storageOptions = storageOptions.Value;
        _logger = logger;
    }

    public async Task<string> GetHypothesisPromptAsync(
        UserIntent intent, ProjectInfo project,
        IReadOnlyList<CycleSummary> recentCycles, MetricsSnapshot? metrics,
        CancellationToken ct = default)
    {
        var basePrompt = await _static.GetHypothesisPromptAsync(intent, project, recentCycles, metrics, ct);
        var cache = await GetOrBuildCacheAsync(project, ct);
        return cache?.HypothesisInstructions is { Length: > 0 }
            ? basePrompt + "\n\n" + cache.HypothesisInstructions
            : basePrompt;
    }

    public async Task<string> GetMutationPromptAsync(
        Hypothesis hypothesis, ProjectInfo project,
        CancellationToken ct = default)
    {
        var basePrompt = await _static.GetMutationPromptAsync(hypothesis, project, ct);
        var cache = await GetOrBuildCacheAsync(project, ct);
        return cache?.MutationInstructions is { Length: > 0 }
            ? basePrompt + "\n\n" + cache.MutationInstructions
            : basePrompt;
    }

    public async Task<string> GetTestGenerationPromptAsync(
        ChangeRecord appliedChange, ProjectInfo project, string hypothesisRationale,
        CancellationToken ct = default)
    {
        var basePrompt = await _static.GetTestGenerationPromptAsync(appliedChange, project, hypothesisRationale, ct);
        var cache = await GetOrBuildCacheAsync(project, ct);
        return cache?.TestGenerationInstructions is { Length: > 0 }
            ? basePrompt + "\n\n" + cache.TestGenerationInstructions
            : basePrompt;
    }

    public async Task<string> GetEvaluationPromptAsync(
        Hypothesis hypothesis, TestSuite testResults, TestSuite? baseline,
        CancellationToken ct = default)
    {
        var basePrompt = await _static.GetEvaluationPromptAsync(hypothesis, testResults, baseline, ct);
        var cache = await GetOrBuildCacheAsync(project: null, ct);
        return cache?.EvaluationInstructions is { Length: > 0 }
            ? basePrompt + "\n\n" + cache.EvaluationInstructions
            : basePrompt;
    }

    private async Task<AdaptivePromptCache?> GetOrBuildCacheAsync(ProjectInfo? project, CancellationToken ct)
    {
        if (_cache != null)
            return _cache;

        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_cache != null)
                return _cache;

            var hash = ComputeProjectHash(project);
            var cacheFile = Path.Combine(_storageOptions.PromptsPath, $"{hash}.json");

            // Tenter de lire le cache existant
            if (File.Exists(cacheFile))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(cacheFile, ct);
                    _cache = JsonSerializer.Deserialize<AdaptivePromptCache>(json);
                    if (_cache != null)
                    {
                        _logger.LogDebug("Cache de prompts adaptatifs chargé depuis {File}", cacheFile);
                        return _cache;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Impossible de lire le cache adaptatif {File} — régénération.", cacheFile);
                }
            }

            if (project == null)
                return null;

            // Générer le cache via Claude (mode --print)
            _logger.LogInformation("Génération des instructions adaptatives pour le projet {Type} ({Language})...",
                project.Type, project.Language);

            var metaPrompt = BuildMetaPrompt(project);
            var result = await _executor.ExecuteAsync(metaPrompt, ct: ct);

            if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
            {
                _logger.LogWarning("Génération des instructions adaptatives échouée — utilisation des prompts statiques.");
                return null;
            }

            // Parser la réponse JSON
            var parsed = ParseAdaptiveCacheFromOutput(result.Output);
            if (parsed == null)
            {
                _logger.LogWarning("Impossible de parser la réponse adaptative — utilisation des prompts statiques.");
                return null;
            }

            // Persister le cache
            Directory.CreateDirectory(_storageOptions.PromptsPath);
            await File.WriteAllTextAsync(cacheFile,
                JsonSerializer.Serialize(parsed, new JsonSerializerOptions { WriteIndented = true }),
                ct);

            _cache = parsed;
            _logger.LogInformation("Cache de prompts adaptatifs généré et sauvegardé dans {File}", cacheFile);
            return _cache;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private static string BuildMetaPrompt(ProjectInfo project)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are analyzing a {project.Type} project (language: {project.Language}, framework: {project.Framework ?? "none"}) to generate specialized instructions for an AI-driven code improvement system.");
        sb.AppendLine();
        sb.AppendLine($"Project path: {project.ProjectPath}");
        if (project.SourcePatterns.Count > 0)
            sb.AppendLine($"Source patterns: {string.Join(", ", project.SourcePatterns)}");
        if (!string.IsNullOrEmpty(project.TestCommand))
            sb.AppendLine($"Test command: {project.TestCommand}");
        sb.AppendLine();
        sb.AppendLine("Explore the project structure briefly (read a few key files) and return a JSON object with these 4 fields:");
        sb.AppendLine("- hypothesisInstructions: project-specific guidance for generating good improvement hypotheses (max 200 words)");
        sb.AppendLine("- mutationInstructions: project-specific guidance for applying code mutations (coding conventions, patterns to follow/avoid, max 200 words)");
        sb.AppendLine("- testGenerationInstructions: project-specific guidance for writing tests (test structure, naming, assertion style, max 200 words)");
        sb.AppendLine("- evaluationInstructions: project-specific guidance for evaluating changes (quality criteria specific to this project, max 200 words)");
        sb.AppendLine();
        sb.AppendLine("Return ONLY the JSON object, no surrounding text:");
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"hypothesisInstructions\": \"...\",");
        sb.AppendLine("  \"mutationInstructions\": \"...\",");
        sb.AppendLine("  \"testGenerationInstructions\": \"...\",");
        sb.AppendLine("  \"evaluationInstructions\": \"...\"");
        sb.AppendLine("}");
        sb.AppendLine("```");
        return sb.ToString();
    }

    private static AdaptivePromptCache? ParseAdaptiveCacheFromOutput(string output)
    {
        try
        {
            // Extraire le bloc JSON (avec ou sans balises markdown)
            var start = output.IndexOf('{');
            var end = output.LastIndexOf('}');
            if (start < 0 || end < 0 || end <= start)
                return null;

            var json = output[start..(end + 1)];
            return JsonSerializer.Deserialize<AdaptivePromptCache>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeProjectHash(ProjectInfo? project)
    {
        if (project == null)
            return "default";

        var key = $"{project.ProjectPath}|{project.Type}|{project.Language}|{project.Framework}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}

/// <summary>
/// Modèle de cache pour les instructions adaptatives générées par Claude.
/// </summary>
public sealed class AdaptivePromptCache
{
    public string HypothesisInstructions { get; set; } = "";
    public string MutationInstructions { get; set; } = "";
    public string TestGenerationInstructions { get; set; } = "";
    public string EvaluationInstructions { get; set; } = "";
}
