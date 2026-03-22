using AutoLoop.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AutoLoop.Evaluation;

public static class EvaluationExtensions
{
    public static IServiceCollection AddAutoLoopEvaluation(this IServiceCollection services)
    {
        services.AddSingleton<IStatisticalTestSuite, MathNetStatisticalTestSuite>();
        services.AddSingleton<IDecisionEngine, ConservativeDecisionEngine>();
        services.AddSingleton<IEvaluationEngine, EvaluationEngine>();
        return services;
    }
}
