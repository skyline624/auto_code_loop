using AutoLoop.Core.Interfaces;
using AutoLoop.Hypothesis.Options;
using Microsoft.Extensions.DependencyInjection;

namespace AutoLoop.Hypothesis;

public static class HypothesisExtensions
{
    public static IServiceCollection AddAutoLoopHypothesis(this IServiceCollection services)
    {
        services.AddSingleton<IMetricsAnalyzer, BaselineMetricsAnalyzer>();
        services.AddSingleton<IErrorPatternAnalyzer, JournalErrorPatternAnalyzer>();
        services.AddSingleton<IHistoryAnalyzer, JournalHistoryAnalyzer>();
        services.AddSingleton<IHypothesisRanker, WeightedHypothesisRanker>();
        services.AddSingleton<IHypothesisEngine, HypothesisEngine>();
        return services;
    }
}
