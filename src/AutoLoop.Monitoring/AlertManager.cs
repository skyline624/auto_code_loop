using AutoLoop.Core.Interfaces;
using AutoLoop.Core.Models;
using Microsoft.Extensions.Logging;

namespace AutoLoop.Monitoring;

/// <summary>
/// Gestionnaire d'alertes. Envoie des alertes via les handlers configurés.
/// Handlers disponibles : log (toujours actif), webhook (optionnel), fichier.
/// </summary>
public sealed class LoggingAlertManager : IAlertManager
{
    private readonly ILogger<LoggingAlertManager> _logger;

    public LoggingAlertManager(ILogger<LoggingAlertManager> logger)
    {
        _logger = logger;
    }

    public Task SendAlertAsync(
        AlertSeverity severity,
        string title,
        string message,
        CancellationToken ct = default)
    {
        var logLevel = severity switch
        {
            AlertSeverity.Info => LogLevel.Information,
            AlertSeverity.Warning => LogLevel.Warning,
            AlertSeverity.Critical => LogLevel.Critical,
            _ => LogLevel.Warning
        };

        _logger.Log(logLevel,
            "🔔 ALERTE [{Severity}] {Title}: {Message}",
            severity, title, message);

        return Task.CompletedTask;
    }
}
