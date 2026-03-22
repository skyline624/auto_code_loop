using AutoLoop.Core.Interfaces;
using AutoLoop.Core.Options;
using Microsoft.Extensions.DependencyInjection;

namespace AutoLoop.Mutation;

public static class MutationExtensions
{
    public static IServiceCollection AddAutoLoopMutation(this IServiceCollection services)
    {
        // Stratégies de mutation (toutes enregistrées, MutationEngine sélectionne la bonne)
        services.AddSingleton<ICodeMutationStrategy, DocumentationRefactoringStrategy>();
        services.AddSingleton<ICodeMutationStrategy, LinqOptimizationStrategy>();
        services.AddSingleton<ICodeMutationStrategy, CacheIntroductionStrategy>();

        services.AddSingleton<ICodeValidator, RoslynCodeValidator>();
        services.AddSingleton<IChangeTracker, JsonlChangeTracker>();
        services.AddSingleton<IMutationEngine, MutationEngine>();
        return services;
    }
}
