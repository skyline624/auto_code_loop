using AutoLoop.ClaudeCode.Options;
using AutoLoop.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AutoLoop.ClaudeCode;

/// <summary>
/// Extensions DI pour le module Claude Code.
/// </summary>
public static class ClaudeCodeExtensions
{
    public static IServiceCollection AddAutoLoopClaudeCode(this IServiceCollection services)
    {
        services.AddOptions<ClaudeCodeOptions>();
        services.AddSingleton<IClaudeCodeExecutor, ClaudeCodeExecutor>();
        services.AddSingleton<ICycleMemory, CycleMemory>();
        services.AddSingleton<IIntentPreserver, IntentPreserver>();
        return services;
    }
}
