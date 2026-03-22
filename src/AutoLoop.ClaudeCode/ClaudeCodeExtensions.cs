using AutoLoop.ClaudeCode.Options;
using AutoLoop.Core.Interfaces;
using AutoLoop.Core.Prompts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

        // Prompts : static (base) + adaptatif (Claude-powered)
        services.AddSingleton<StaticPromptProvider>();
        services.AddSingleton<AdaptivePromptProvider>();
        services.AddSingleton<IPromptProvider>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ClaudeCodeOptions>>();
            return options.Value.UseAdaptivePrompts
                ? sp.GetRequiredService<AdaptivePromptProvider>()
                : (IPromptProvider)sp.GetRequiredService<StaticPromptProvider>();
        });

        return services;
    }
}
