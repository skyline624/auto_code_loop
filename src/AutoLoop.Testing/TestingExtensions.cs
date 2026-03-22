using AutoLoop.Core.Interfaces;
using AutoLoop.Core.Options;
using Microsoft.Extensions.DependencyInjection;

namespace AutoLoop.Testing;

public static class TestingExtensions
{
    public static IServiceCollection AddAutoLoopTesting(this IServiceCollection services)
    {
        // Options
        services.AddOptions<Options.TestingOptions>();

        // Language-agnostic test runner (nouveau)
        services.AddSingleton<ITestRunner, LanguageAgnosticTestRunner>();

        // Runners spécifiques par langue (optionnels)
        services.AddSingleton<ILanguageTestRunner, DotNetLanguageTestRunner>();

        // Services de support
        services.AddSingleton<IRegressionTestRunner, BaselineRegressionTestRunner>();
        services.AddSingleton<IBaselineStore, JsonFileBaselineStore>();

        // Runners .NET spécifiques (pour le projet AutoLoop lui-même)
        services.AddSingleton<IUnitTestRunner, DotnetTestRunner>();
        services.AddSingleton<IPerformanceTestRunner, InProcessPerformanceTestRunner>();

        return services;
    }

    /// <summary>
    /// Enregistre uniquement le runner language-agnostic (sans les runners .NET spécifiques).
    /// Utile pour les projets non-.NET.
    /// </summary>
    public static IServiceCollection AddAutoLoopTestingLanguageAgnostic(this IServiceCollection services)
    {
        // Options
        services.AddOptions<Options.TestingOptions>();

        // Language-agnostic test runner
        services.AddSingleton<ITestRunner, LanguageAgnosticTestRunner>();

        // Services de support
        services.AddSingleton<IRegressionTestRunner, BaselineRegressionTestRunner>();
        services.AddSingleton<IBaselineStore, JsonFileBaselineStore>();

        return services;
    }
}
