using AutoLoop.Core.Interfaces;
using AutoLoop.Versioning.Options;
using Microsoft.Extensions.DependencyInjection;

namespace AutoLoop.Versioning;

public static class VersioningExtensions
{
    public static IServiceCollection AddAutoLoopVersioning(this IServiceCollection services)
    {
        services.AddSingleton<ILocalGitOperations, LibGit2SharpOperations>();
        services.AddSingleton<IVersioningBackend, GitHubBackend>();
        return services;
    }
}
