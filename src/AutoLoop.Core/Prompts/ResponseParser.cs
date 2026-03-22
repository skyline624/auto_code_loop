using System.Text.Json;
using System.Text.Json.Serialization;
using AutoLoop.Core.Models;

namespace AutoLoop.Core.Prompts;

/// <summary>
/// Parser pour les réponses JSON de Claude Code.
/// </summary>
public sealed class ResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Parse une réponse d'hypothèses depuis Claude Code.
    /// </summary>
    public ClaudeHypothesisResponse? ParseHypothesisResponse(string output)
    {
        var json = ExtractJson(output);
        if (json == null) return null;

        try
        {
            return JsonSerializer.Deserialize<ClaudeHypothesisResponse>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Impossible de parser la réponse d'hypothèses: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Parse une réponse de mutation depuis Claude Code.
    /// </summary>
    public ClaudeMutationResponse? ParseMutationResponse(string output)
    {
        var json = ExtractJson(output);
        if (json == null) return null;

        try
        {
            return JsonSerializer.Deserialize<ClaudeMutationResponse>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Impossible de parser la réponse de mutation: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Parse une réponse d'évaluation depuis Claude Code.
    /// </summary>
    public ClaudeEvaluationResponse? ParseEvaluationResponse(string output)
    {
        var json = ExtractJson(output);
        if (json == null) return null;

        try
        {
            return JsonSerializer.Deserialize<ClaudeEvaluationResponse>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Impossible de parser la réponse d'évaluation: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Extrait le JSON d'une réponse qui peut contenir du texte avant/après.
    /// </summary>
    private static string? ExtractJson(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        // Chercher un bloc JSON entre ```json et ```
        var jsonBlockStart = output.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (jsonBlockStart >= 0)
        {
            jsonBlockStart += 7; // Skip "```json"
            var jsonBlockEnd = output.IndexOf("```", jsonBlockStart);
            if (jsonBlockEnd > jsonBlockStart)
            {
                return output[jsonBlockStart..jsonBlockEnd].Trim();
            }
        }

        // Chercher un bloc JSON entre ``` et ```
        var codeBlockStart = output.IndexOf("```");
        if (codeBlockStart >= 0)
        {
            codeBlockStart += 3;
            // Skip le langage s'il y en a un
            var newlineIndex = output.IndexOf('\n', codeBlockStart);
            if (newlineIndex > codeBlockStart && newlineIndex - codeBlockStart < 20)
            {
                codeBlockStart = newlineIndex + 1;
            }
            var codeBlockEnd = output.IndexOf("```", codeBlockStart);
            if (codeBlockEnd > codeBlockStart)
            {
                return output[codeBlockStart..codeBlockEnd].Trim();
            }
        }

        // Chercher un objet JSON direct
        var objectStart = output.IndexOf('{');
        if (objectStart >= 0)
        {
            // Trouver la fin de l'objet
            var depth = 0;
            var inString = false;
            var escape = false;

            for (var i = objectStart; i < output.Length; i++)
            {
                var c = output[i];

                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString) continue;

                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return output[objectStart..(i + 1)];
                    }
                }
            }
        }

        // Pas de JSON trouvé
        return null;
    }

    /// <summary>
    /// Convertit une réponse d'hypothèse en liste d'hypothèses du domaine.
    /// </summary>
    public IReadOnlyList<Hypothesis> ToHypotheses(ClaudeHypothesisResponse response, CycleId cycleId)
    {
        return response.Hypotheses
            .Select((h, index) => new Hypothesis
            {
                Id = h.Id ?? $"hypo-{cycleId.Value:N}-{index}",
                CycleId = cycleId,
                Type = MapHypothesisType(h.Description),
                TargetFile = h.TargetFiles.FirstOrDefault() ?? string.Empty,
                TargetMethod = h.TargetFiles.Skip(1).FirstOrDefault(),
                Rationale = h.Rationale ?? h.Description,
                Priority = 1.0 - (h.Risk?.ToLowerInvariant() switch
                {
                    "low" => 0.1,
                    "high" => 0.5,
                    _ => 0.3
                }),
                ExpectedImpact = h.ExpectedImpact / 10.0,
                ConfidenceScore = h.Confidence,
                Evidence = new Dictionary<string, object>
                {
                    ["source"] = "claude-code",
                    ["target_files"] = h.TargetFiles.ToList(),
                    ["risk_level"] = h.Risk ?? "medium"
                },
                GeneratedAt = DateTimeOffset.UtcNow
            })
            .ToList();
    }

    private static HypothesisType MapHypothesisType(string description)
    {
        var lower = description.ToLowerInvariant();

        if (lower.Contains("memory") || lower.Contains("allocation"))
            return HypothesisType.MemoryLeak;

        if (lower.Contains("performance") || lower.Contains("speed") || lower.Contains("latency"))
            return HypothesisType.PerformanceBottleneck;

        if (lower.Contains("bug") || lower.Contains("fix") || lower.Contains("error"))
            return HypothesisType.RecurringError;

        if (lower.Contains("coverage") || lower.Contains("test"))
            return HypothesisType.CoverageGap;

        if (lower.Contains("refactor") || lower.Contains("clean") || lower.Contains("smell"))
            return HypothesisType.CodeSmell;

        if (lower.Contains("algorithm") || lower.Contains("optimize"))
            return HypothesisType.UnoptimizedAlgorithm;

        return HypothesisType.PerformanceBottleneck;
    }
}