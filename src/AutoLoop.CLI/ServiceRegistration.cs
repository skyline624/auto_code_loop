using AutoLoop.ClaudeCode;
using AutoLoop.ClaudeCode.Options;
using AutoLoop.Core;
using AutoLoop.Core.Options;
using AutoLoop.Evaluation;
using AutoLoop.Evaluation.Options;
using AutoLoop.Hypothesis;
using AutoLoop.Hypothesis.Options;
using AutoLoop.Logging;
using AutoLoop.Monitoring;
using AutoLoop.Monitoring.Options;
using AutoLoop.Mutation;
using AutoLoop.ProjectDetection;
using AutoLoop.ProjectDetection.Options;
using AutoLoop.Rollback;
using AutoLoop.Testing;
using AutoLoop.Testing.Options;
using AutoLoop.Versioning;
using AutoLoop.Versioning.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prometheus;

namespace AutoLoop.CLI;

public static class ServiceRegistration
{
    public static IHostBuilder ConfigureAutoLoop(this IHostBuilder builder)
    {
        return builder
            .ConfigureServices((ctx, services) =>
            {
                var config = ctx.Configuration;

                // ── Options ──────────────────────────────────────────────────
                services.Configure<CycleOptions>(config.GetSection(CycleOptions.Section));
                services.Configure<StorageOptions>(config.GetSection(StorageOptions.Section));
                services.Configure<GitHubOptions>(config.GetSection(GitHubOptions.Section));
                services.Configure<LocalGitOptions>(config.GetSection(LocalGitOptions.Section));
                services.Configure<HypothesisOptions>(config.GetSection(HypothesisOptions.Section));
                services.Configure<EvaluationOptions>(config.GetSection(EvaluationOptions.Section));
                services.Configure<TestingOptions>(config.GetSection(TestingOptions.Section));
                services.Configure<MonitoringOptions>(config.GetSection(MonitoringOptions.Section));
                services.Configure<ClaudeCodeOptions>(config.GetSection(ClaudeCodeOptions.Section));
                services.Configure<ProjectDetectionOptions>(config.GetSection(ProjectDetectionOptions.Section));

                // ── Modules ──────────────────────────────────────────────────
                services.AddAutoLoopLogging();
                services.AddAutoLoopMonitoring();
                services.AddAutoLoopVersioning();
                services.AddAutoLoopRollback();
                services.AddAutoLoopHypothesis();
                services.AddAutoLoopMutation();
                services.AddAutoLoopTesting();
                services.AddAutoLoopEvaluation();

                // ── Nouveaux modules (Claude Code + Project Detection) ─────────
                services.AddAutoLoopClaudeCode();
                services.AddAutoLoopProjectDetection();

                // ── Orchestrateur principal ───────────────────────────────────
                services.AddHostedService<CycleOrchestrator>();
            });
    }
}