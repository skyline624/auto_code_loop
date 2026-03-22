using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AutoLoop.ClaudeCode.Options;
using AutoLoop.Core.Interfaces;
using AutoLoop.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoLoop.ClaudeCode;

/// <summary>
/// Implémentation de l'exécuteur Claude Code CLI via subprocess.
/// </summary>
public sealed class ClaudeCodeExecutor : IClaudeCodeExecutor
{
    private readonly ILogger<ClaudeCodeExecutor> _logger;
    private readonly ClaudeCodeOptions _options;

    public ClaudeCodeExecutor(
        IOptions<ClaudeCodeOptions> options,
        ILogger<ClaudeCodeExecutor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ClaudeCodeResult> ExecuteAsync(
        string prompt,
        IEnumerable<string>? contextFiles = null,
        IReadOnlyDictionary<string, string>? variables = null,
        CancellationToken ct = default)
    {
        // Injecter les variables dans le prompt
        var finalPrompt = InjectVariables(prompt, variables);

        // Préparer les arguments
        var args = BuildArguments(contextFiles);

        _logger.LogDebug("Exécution Claude Code: {Executable} {Args}", _options.Executable, string.Join(" ", args));
        _logger.LogDebug("Prompt ({Length} chars): {Prompt}", finalPrompt.Length, finalPrompt[..Math.Min(200, finalPrompt.Length)]);

        var startedAt = DateTimeOffset.UtcNow;
        var (exitCode, stdout, stderr) = await RunClaudeCodeAsync(args, finalPrompt, ct, Directory.GetCurrentDirectory(), _options.TimeoutMs);
        var completedAt = DateTimeOffset.UtcNow;

        // Parser les métriques de tokens
        var (inputTokens, outputTokens) = ParseTokenCounts(stderr);

        var result = new ClaudeCodeResult
        {
            Output = stdout,
            RawPrompt = finalPrompt,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            ExitCode = exitCode,
            ErrorOutput = exitCode != 0 ? stderr : null,
            Duration = completedAt - startedAt,
            InputTokens = inputTokens,
            OutputTokens = outputTokens
        };

        if (result.Success)
        {
            _logger.LogDebug("Claude Code terminé avec succès en {Duration}ms. Tokens: {Input} input, {Output} output",
                result.Duration.TotalMilliseconds, inputTokens, outputTokens);
        }
        else
        {
            _logger.LogError("Claude Code a échoué avec le code {ExitCode}. Stderr: {Stderr}",
                exitCode, stderr[..Math.Min(500, stderr.Length)]);
        }

        return result;
    }

    public async Task<ClaudeCodeResult> ExecuteWithConfirmationAsync(
        string prompt,
        string confirmationPrompt,
        IEnumerable<string>? contextFiles = null,
        CancellationToken ct = default)
    {
        // D'abord exécuter le prompt principal
        var result = await ExecuteAsync(prompt, contextFiles, null, ct);

        if (!result.Success)
            return result;

        // Ensuite demander confirmation
        var confirmationResult = await ExecuteAsync(confirmationPrompt, contextFiles, null, ct);

        // Retourner le résultat combiné
        return new ClaudeCodeResult
        {
            Output = result.Output + "\n\n--- Confirmation ---\n" + confirmationResult.Output,
            RawPrompt = result.RawPrompt,
            StartedAt = result.StartedAt,
            CompletedAt = confirmationResult.CompletedAt,
            ExitCode = confirmationResult.ExitCode,
            ErrorOutput = confirmationResult.ErrorOutput,
            Duration = confirmationResult.CompletedAt - result.StartedAt,
            InputTokens = result.InputTokens + confirmationResult.InputTokens,
            OutputTokens = result.OutputTokens + confirmationResult.OutputTokens
        };
    }

    public async Task<ClaudeCodeResult> ExecuteAgenticAsync(
        string prompt,
        string workingDirectory,
        IEnumerable<string>? contextFiles = null,
        CancellationToken ct = default)
    {
        var args = BuildAgenticArguments(contextFiles);

        _logger.LogDebug("Exécution Claude Code agentique dans {WorkingDir}", workingDirectory);
        _logger.LogDebug("Prompt agentique ({Length} chars): {Prompt}", prompt.Length, prompt[..Math.Min(200, prompt.Length)]);

        var startedAt = DateTimeOffset.UtcNow;
        var (exitCode, stdout, stderr) = await RunClaudeCodeAsync(args, prompt, ct, workingDirectory, _options.AgenticTimeoutMs);
        var completedAt = DateTimeOffset.UtcNow;

        var (inputTokens, outputTokens) = ParseTokenCounts(stderr);

        var result = new ClaudeCodeResult
        {
            Output = stdout,
            RawPrompt = prompt,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            ExitCode = exitCode,
            ErrorOutput = exitCode != 0 ? stderr : null,
            Duration = completedAt - startedAt,
            InputTokens = inputTokens,
            OutputTokens = outputTokens
        };

        if (result.Success)
            _logger.LogInformation("Claude Code agentique terminé en {Duration}ms.", result.Duration.TotalMilliseconds);
        else
            _logger.LogError("Claude Code agentique a échoué (code {ExitCode}). Stderr: {Stderr}",
                exitCode, stderr[..Math.Min(500, stderr.Length)]);

        return result;
    }

    private List<string> BuildAgenticArguments(IEnumerable<string>? contextFiles)
    {
        // Mode agentique : pas de --print, Claude utilise ses outils natifs (Edit/Write/Bash)
        var args = new List<string> { "--dangerously-skip-permissions" };

        if (!string.IsNullOrEmpty(_options.DefaultModel) && _options.DefaultModel != "claude-sonnet-4-6")
        {
            args.Add("--model");
            args.Add(_options.DefaultModel);
        }

        if (contextFiles != null)
        {
            var filesToAdd = contextFiles
                .Where(f => !string.IsNullOrEmpty(f))
                .Take(_options.ContextFileLimit)
                .ToList();

            foreach (var file in filesToAdd)
            {
                args.Add("--context");
                args.Add(file);
            }
        }

        return args;
    }

    private List<string> BuildArguments(IEnumerable<string>? contextFiles)
    {
        var args = new List<string> { "--print" };

        // Ajouter le modèle si différent du défaut
        if (!string.IsNullOrEmpty(_options.DefaultModel) && _options.DefaultModel != "claude-sonnet-4-6")
        {
            args.Add("--model");
            args.Add(_options.DefaultModel);
        }

        // Ajouter la limite de tokens
        if (_options.MaxTokens > 0)
        {
            args.Add("--max-tokens");
            args.Add(_options.MaxTokens.ToString());
        }

        // Ajouter les fichiers de contexte
        if (contextFiles != null)
        {
            var filesToAdd = contextFiles
                .Where(f => !string.IsNullOrEmpty(f))
                .Take(_options.ContextFileLimit)
                .ToList();

            foreach (var file in filesToAdd)
            {
                args.Add("--context");
                args.Add(file);
            }
        }

        return args;
    }

    private async Task<(int exitCode, string stdout, string stderr)> RunClaudeCodeAsync(
        List<string> args,
        string prompt,
        CancellationToken ct,
        string workingDirectory,
        int timeoutMs)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _options.Executable,
                Arguments = string.Join(" ", args.Select(EscapeArgument)),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            }
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Impossible de démarrer Claude Code CLI. Vérifiez que '{Executable}' est installé et dans le PATH.",
                _options.Executable);
            throw new InvalidOperationException($"Claude Code CLI non trouvé: {_options.Executable}", ex);
        }

        // Envoyer le prompt via stdin
        await process.StandardInput.WriteAsync(prompt.AsMemory(), ct);
        process.StandardInput.Close();

        // Lire les sorties de manière asynchrone
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        // Attendre avec timeout
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Claude Code a dépassé le timeout de {Timeout}ms. Processus tué.", timeoutMs);
            process.Kill();
            await process.WaitForExitAsync(ct);
            throw new TimeoutException($"Claude Code a dépassé le timeout de {timeoutMs}ms");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return (process.ExitCode, stdout, stderr);
    }

    private static string InjectVariables(string prompt, IReadOnlyDictionary<string, string>? variables)
    {
        if (variables == null || variables.Count == 0)
            return prompt;

        var result = new StringBuilder(prompt);
        foreach (var (key, value) in variables)
        {
            result.Replace($"{{{{${key}}}}}", value);
        }
        return result.ToString();
    }

    private (int inputTokens, int outputTokens) ParseTokenCounts(string stderr)
    {
        // Parser les métriques de tokens depuis stderr si disponibles
        // Format typique: "Input tokens: 1234 Output tokens: 567"
        var inputTokens = 0;
        var outputTokens = 0;

        try
        {
            // Chercher des patterns comme "Input tokens:" ou "input_tokens"
            var inputMatch = System.Text.RegularExpressions.Regex.Match(
                stderr, @"input[s]?\s*tokens?\s*[:=]?\s*(\d+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (inputMatch.Success && int.TryParse(inputMatch.Groups[1].Value, out var input))
            {
                inputTokens = input;
            }

            var outputMatch = System.Text.RegularExpressions.Regex.Match(
                stderr, @"output[s]?\s*tokens?\s*[:=]?\s*(\d+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (outputMatch.Success && int.TryParse(outputMatch.Groups[1].Value, out var output))
            {
                outputTokens = output;
            }
        }
        catch
        {
            // Ignorer les erreurs de parsing
        }

        return (inputTokens, outputTokens);
    }

    private static string EscapeArgument(string arg)
    {
        // Échapper les arguments pour la ligne de commande
        if (arg.Contains(' ') || arg.Contains('"') || arg.Contains('\\'))
        {
            return $"\"{arg.Replace("\"", "\\\"")}\"";
        }
        return arg;
    }
}