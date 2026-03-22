using Microsoft.Extensions.DependencyInjection;
using AutoLoop.Core.Interfaces;
using AutoLoop.ProjectDetection.LanguageDetectors;
using AutoLoop.ProjectDetection.Options;

namespace AutoLoop.ProjectDetection;

/// <summary>
/// Extensions DI pour le module de détection de projet.
/// </summary>
public static class ProjectDetectionExtensions
{
    public static IServiceCollection AddAutoLoopProjectDetection(this IServiceCollection services)
    {
        // Options
        services.AddOptions<ProjectDetectionOptions>();

        // Services
        services.AddSingleton<IProjectDetector, ProjectDetector>();

        // Détecteurs de langage (ordre de priorité)
        services.AddSingleton<ILanguageDetector, NodeJsDetector>();
        services.AddSingleton<ILanguageDetector, PythonDetector>();
        services.AddSingleton<ILanguageDetector, DotNetDetector>();
        services.AddSingleton<ILanguageDetector, RustDetector>();
        services.AddSingleton<ILanguageDetector, GoDetector>();
        services.AddSingleton<ILanguageDetector, JavaDetector>();
        services.AddSingleton<ILanguageDetector, RubyDetector>();
        services.AddSingleton<ILanguageDetector, PhpDetector>();

        return services;
    }
}