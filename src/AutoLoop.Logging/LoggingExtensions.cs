using AutoLoop.Core.Interfaces;
using AutoLoop.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Formatting.Compact;

namespace AutoLoop.Logging;

public static class LoggingExtensions
{
    public static IHostBuilder UseAutoLoopSerilog(this IHostBuilder builder)
        => builder.UseSerilog((ctx, services, config) => config
            .ReadFrom.Configuration(ctx.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .Enrich.WithProperty("Application", "AutoLoop"));

    public static IServiceCollection AddAutoLoopLogging(this IServiceCollection services)
    {
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton<IAuditTrail, JsonlAuditTrail>();
        services.AddSingleton<ICycleJournal, JsonlCycleJournal>();
        return services;
    }
}
