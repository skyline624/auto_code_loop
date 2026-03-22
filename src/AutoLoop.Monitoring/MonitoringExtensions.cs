using AutoLoop.Core.Interfaces;
using AutoLoop.Monitoring.Options;
using Microsoft.Extensions.DependencyInjection;

namespace AutoLoop.Monitoring;

public static class MonitoringExtensions
{
    public static IServiceCollection AddAutoLoopMonitoring(this IServiceCollection services)
    {
        services.AddSingleton<PrometheusMetricsRegistry>();
        services.AddSingleton<IMetricsRegistry>(sp => sp.GetRequiredService<PrometheusMetricsRegistry>());
        services.AddSingleton<IAlertManager, LoggingAlertManager>();
        services.AddHostedService<SystemMonitor>();
        return services;
    }
}
