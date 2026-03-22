using AutoLoop.CLI;
using AutoLoop.Core.Models;
using AutoLoop.Core.Options;
using AutoLoop.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prometheus;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;

// ── Commandes CLI ─────────────────────────────────────────────────────────────

var rootCommand = new RootCommand("AutoLoop — Framework d'auto-amélioration multi-langage via Claude Code");

var intentArgument = new Argument<string>(
    name: "intent",
    description: "L'intention d'amélioration (ex: 'reduce memory usage in API handlers', 'optimize database queries', 'improve test coverage')");

var dryRunOption = new Option<bool>(
    name: "--dry-run",
    description: "Exécuter en mode simulation sans modifications réelles",
    getDefaultValue: () => false);

var maxCyclesOption = new Option<int?>(
    name: "--max-cycles",
    description: "Nombre maximum de cycles (null = infini)");

var configPathOption = new Option<string>(
    name: "--config",
    description: "Chemin vers appsettings.json",
    getDefaultValue: () => "appsettings.json");

var interactiveOption = new Option<bool>(
    name: "--interactive",
    description: "Mode interactif avec confirmation avant chaque action",
    getDefaultValue: () => false);

var projectPathOption = new Option<string?>(
    name: "--project-path",
    description: "Chemin vers le projet cible (défaut: répertoire courant)");

rootCommand.AddArgument(intentArgument);
rootCommand.AddOption(dryRunOption);
rootCommand.AddOption(maxCyclesOption);
rootCommand.AddOption(configPathOption);
rootCommand.AddOption(interactiveOption);
rootCommand.AddOption(projectPathOption);

rootCommand.SetHandler(async (context) =>
{
    var intent = context.ParseResult.GetValueForArgument(intentArgument);
    var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
    var maxCycles = context.ParseResult.GetValueForOption(maxCyclesOption);
    var configPath = context.ParseResult.GetValueForOption(configPathOption);
    var interactive = context.ParseResult.GetValueForOption(interactiveOption);
    var projectPath = context.ParseResult.GetValueForOption(projectPathOption);

    Console.WriteLine("╔══════════════════════════════════════════╗");
    Console.WriteLine("║     AutoLoop — Auto-Amélioration         ║");
    Console.WriteLine("║    Multi-Langage via Claude Code         ║");
    Console.WriteLine("╚══════════════════════════════════════════╝");
    Console.WriteLine();
    Console.WriteLine($"Intention: {intent}");

    var targetPath = projectPath ?? Directory.GetCurrentDirectory();
    Console.WriteLine($"Projet cible: {targetPath}");
    Console.WriteLine();

    if (dryRun)
        Console.WriteLine("⚠  Mode DryRun activé — aucune modification de code.");
    if (interactive)
        Console.WriteLine("ℹ  Mode interactif activé — confirmation requise avant chaque action.");
    if (maxCycles.HasValue)
        Console.WriteLine($"ℹ  Nombre de cycles: {maxCycles.Value}");

    var host = BuildHost(configPath ?? "appsettings.json", dryRun, maxCycles, interactive, intent, targetPath);
    await host.RunAsync(context.GetCancellationToken());
});

// Parsing et invocation
return await new CommandLineBuilder(rootCommand)
    .UseDefaults()
    .Build()
    .InvokeAsync(args);

// ── Construction de l'hôte ────────────────────────────────────────────────────

static IHost BuildHost(string configPath, bool dryRun, int? maxCycles, bool interactive, string intent, string projectPath)
{
    return Host.CreateDefaultBuilder()
        .UseAutoLoopSerilog()
        .ConfigureAppConfiguration((ctx, config) =>
        {
            config.SetBasePath(Directory.GetCurrentDirectory());
            config.AddJsonFile(configPath, optional: false, reloadOnChange: false);
            config.AddJsonFile(
                $"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json",
                optional: true,
                reloadOnChange: false);
            config.AddEnvironmentVariables();

            // Surcharges CLI
            var overrides = new Dictionary<string, string?>();
            if (dryRun) overrides["Cycle:DryRun"] = "true";
            if (maxCycles.HasValue) overrides["Cycle:MaxCycles"] = maxCycles.Value.ToString();
            if (interactive) overrides["Cycle:InteractiveMode"] = "true";
            overrides["Cycle:TargetProjectPath"] = projectPath;
            config.AddInMemoryCollection(overrides);
        })
        .ConfigureAutoLoop()
        .ConfigureServices((ctx, services) =>
        {
            // Enregistrer l'intention utilisateur comme singleton
            services.AddSingleton(new UserIntent
            {
                OriginalIntent = intent,
                ExpandedIntent = intent, // Sera expandu par ClaudeCodeExecutor
                CreatedAt = DateTimeOffset.UtcNow,
                CycleId = CycleId.New()
            });

            // Exposition des métriques Prometheus sur un endpoint dédié
            var port = ctx.Configuration.GetValue("Monitoring:PrometheusPort", 9090);
            services.AddSingleton<IHostedService>(_ =>
                new PrometheusServerHostedService(port));
        })
        .Build();
}

// ── Service Prometheus standalone ────────────────────────────────────────────

internal sealed class PrometheusServerHostedService : IHostedService
{
    private readonly int _port;
    private IMetricServer? _server;

    public PrometheusServerHostedService(int port) => _port = port;

    public Task StartAsync(CancellationToken ct)
    {
        try
        {
            _server = new MetricServer(port: _port);
            _server.Start();
            Console.WriteLine($"📊 Métriques Prometheus exposées sur http://localhost:{_port}/metrics");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠  Impossible de démarrer Prometheus sur port {_port} : {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _server?.Stop();
        return Task.CompletedTask;
    }
}