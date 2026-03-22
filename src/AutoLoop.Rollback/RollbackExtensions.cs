using AutoLoop.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AutoLoop.Rollback;

public static class RollbackExtensions
{
    public static IServiceCollection AddAutoLoopRollback(this IServiceCollection services)
    {
        services.AddSingleton<IRollbackPolicy, DefaultRollbackPolicy>();
        services.AddSingleton<IHealthChecker, DefaultHealthChecker>();
        services.AddSingleton<IRollbackManager, RollbackManager>();
        return services;
    }
}
