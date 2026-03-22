using System.Text;
using AutoLoop.Core.Models;

namespace AutoLoop.Core.Prompts;

/// <summary>
/// Templates de prompts pour chaque phase du cycle d'amélioration.
/// </summary>
public static class PromptTemplates
{
    /// <summary>
    /// Génère le prompt pour la Phase 1: Génération d'hypothèses.
    /// </summary>
    public static string GenerateHypotheses(
        UserIntent intent,
        ProjectInfo project,
        IReadOnlyList<CycleSummary> recentCycles,
        MetricsSnapshot? metrics)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"You are analyzing a {project.Type} project to propose improvements.");
        sb.AppendLine();
        sb.AppendLine("**User Intent**:");
        sb.AppendLine($"Original: {intent.OriginalIntent}");
        if (!string.IsNullOrEmpty(intent.ExpandedIntent))
        {
            sb.AppendLine($"Expanded: {intent.ExpandedIntent}");
        }
        sb.AppendLine();
        sb.AppendLine("**Constraints**:");
        if (intent.Constraints.Count == 0)
        {
            sb.AppendLine("- No specific constraints");
        }
        else
        {
            foreach (var constraint in intent.Constraints)
            {
                sb.AppendLine($"- {constraint}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("**Project Context**:");
        sb.AppendLine($"- Type: {project.Type}");
        if (!string.IsNullOrEmpty(project.Language))
            sb.AppendLine($"- Language: {project.Language}");
        if (!string.IsNullOrEmpty(project.Framework))
            sb.AppendLine($"- Framework: {project.Framework}");
        if (!string.IsNullOrEmpty(project.PackageManager))
            sb.AppendLine($"- Package Manager: {project.PackageManager}");
        if (project.SourcePatterns.Count > 0)
            sb.AppendLine($"- Source patterns: {string.Join(", ", project.SourcePatterns)}");
        sb.AppendLine();

        if (recentCycles.Count > 0)
        {
            sb.AppendLine($"**Recent Cycle History** ({recentCycles.Count} cycles):");
            foreach (var cycle in recentCycles.Take(5))
            {
                sb.AppendLine($"- [{cycle.Status}] {cycle.HypothesisSummary ?? "N/A"} → {cycle.Decision ?? "N/A"}");
                if (cycle.ImprovementScore.HasValue)
                {
                    sb.AppendLine($"  Score: {cycle.ImprovementScore.Value:F1}%");
                }
            }
            sb.AppendLine();
        }

        if (metrics != null)
        {
            sb.AppendLine("**Current Metrics**:");
            sb.AppendLine($"- CPU: {metrics.CpuPercent:F1}%");
            sb.AppendLine($"- Memory: {metrics.MemoryMb:F0} MB");
            if (metrics.ResponseTimeP95Ms > 0)
                sb.AppendLine($"- Response time p95: {metrics.ResponseTimeP95Ms:F0} ms");
            sb.AppendLine();
        }

        sb.AppendLine("**Task**: Generate 1-5 hypotheses for improvements that address the user's intent.");
        sb.AppendLine("Each hypothesis should include:");
        sb.AppendLine("- A clear description of the improvement");
        sb.AppendLine("- Target files/modules affected");
        sb.AppendLine("- Expected impact score (1-10)");
        sb.AppendLine("- Confidence level (0.0-1.0)");
        sb.AppendLine("- Risk level (low/medium/high)");
        sb.AppendLine("- Rationale for why this change would help");
        sb.AppendLine();
        sb.AppendLine("Format your response as valid JSON:");
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"hypotheses\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"id\": \"unique-id-1\",");
        sb.AppendLine("      \"description\": \"Clear description of the improvement\",");
        sb.AppendLine("      \"targetFiles\": [\"path/to/file1\", \"path/to/file2\"],");
        sb.AppendLine("      \"expectedImpact\": 8,");
        sb.AppendLine("      \"confidence\": 0.75,");
        sb.AppendLine("      \"risk\": \"medium\",");
        sb.AppendLine("      \"rationale\": \"Why this change will help\"");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        sb.AppendLine("```");

        return sb.ToString();
    }

    /// <summary>
    /// Génère le prompt pour la Phase 2: Application de mutation.
    /// </summary>
    public static string ApplyMutation(Hypothesis hypothesis, ProjectInfo project)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"You are modifying a {project.Type} project to implement an improvement.");
        sb.AppendLine();
        sb.AppendLine("**Hypothesis to implement**:");
        sb.AppendLine($"Description: {hypothesis.Rationale}");
        sb.AppendLine($"Type: {hypothesis.Type}");
        sb.AppendLine($"Target: {hypothesis.TargetFile}");
        if (!string.IsNullOrEmpty(hypothesis.TargetMethod))
            sb.AppendLine($"Method: {hypothesis.TargetMethod}");
        sb.AppendLine($"Expected Impact: {hypothesis.ExpectedImpact:P0}");
        sb.AppendLine($"Confidence: {hypothesis.ConfidenceScore:P0}");
        sb.AppendLine();
        sb.AppendLine("**Files to modify**:");
        sb.AppendLine($"Primary: {hypothesis.TargetFile}");
        sb.AppendLine();
        sb.AppendLine("**Task**: Apply the necessary code changes to implement this improvement.");
        sb.AppendLine();
        sb.AppendLine("Guidelines:");
        sb.AppendLine("1. Make minimal, focused changes");
        sb.AppendLine("2. Preserve existing functionality");
        sb.AppendLine("3. Follow the project's existing code style");
        sb.AppendLine("4. Include necessary imports/dependencies");
        sb.AppendLine("5. Add comments for non-obvious changes");
        sb.AppendLine();
        sb.AppendLine("Format your response as valid JSON:");
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"changes\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"filePath\": \"path/to/file\",");
        sb.AppendLine("      \"changeType\": \"modify\",");
        sb.AppendLine("      \"diff\": \"unified diff format\"");
        sb.AppendLine("    }");
        sb.AppendLine("  ],");
        sb.AppendLine("  \"summary\": \"Brief description of what was changed\"");
        sb.AppendLine("}");
        sb.AppendLine("```");

        return sb.ToString();
    }

    /// <summary>
    /// Génère le prompt pour la Phase 3: Tests (fallback si framework non détecté).
    /// </summary>
    public static string DetectAndRunTests(ProjectInfo project)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"You need to detect and run tests for this {project.Type} project.");
        sb.AppendLine();
        sb.AppendLine("**Project Information**:");
        sb.AppendLine($"- Path: {project.ProjectPath}");
        sb.AppendLine($"- Type: {project.Type}");
        if (!string.IsNullOrEmpty(project.Language))
            sb.AppendLine($"- Language: {project.Language}");
        if (!string.IsNullOrEmpty(project.TestCommand))
            sb.AppendLine($"- Known test command: {project.TestCommand}");
        sb.AppendLine();
        sb.AppendLine("**Task**:");
        sb.AppendLine("1. Identify the test framework used in this project");
        sb.AppendLine("2. Run the appropriate test command");
        sb.AppendLine("3. Report the test results");
        sb.AppendLine();
        sb.AppendLine("Format your response as valid JSON:");
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"testFramework\": \"name of detected framework\",");
        sb.AppendLine("  \"testCommand\": \"command to run tests\",");
        sb.AppendLine("  \"results\": {");
        sb.AppendLine("    \"total\": 10,");
        sb.AppendLine("    \"passed\": 8,");
        sb.AppendLine("    \"failed\": 2,");
        sb.AppendLine("    \"skipped\": 0,");
        sb.AppendLine("    \"allPassed\": false,");
        sb.AppendLine("    \"failedTests\": [\"test1\", \"test2\"]");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine("```");

        return sb.ToString();
    }

    /// <summary>
    /// Génère le prompt pour la Phase 4: Évaluation.
    /// </summary>
    public static string EvaluateChanges(
        Hypothesis hypothesis,
        TestSuite testResults,
        TestSuite? baseline)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are evaluating whether an improvement was successful.");
        sb.AppendLine();
        sb.AppendLine("**Original Hypothesis**:");
        sb.AppendLine($"Description: {hypothesis.Rationale}");
        sb.AppendLine($"Expected Impact: {hypothesis.ExpectedImpact}/10");
        sb.AppendLine($"Confidence: {hypothesis.ConfidenceScore:P0}");
        sb.AppendLine();
        sb.AppendLine("**Test Results**:");
        sb.AppendLine($"- Unit tests: {testResults.UnitTests.Passed}/{testResults.UnitTests.TotalTests} passed");
        sb.AppendLine($"- All passed: {testResults.AllPassed}");
        if (testResults.Regression.Checks.Count > 0)
        {
            var regPassed = testResults.Regression.Checks.Count(c => c.Passed);
            sb.AppendLine($"- Regression: {regPassed}/{testResults.Regression.Checks.Count} passed");
        }
        sb.AppendLine();

        if (baseline != null)
        {
            sb.AppendLine("**Baseline Comparison**:");
            sb.AppendLine($"- Baseline unit tests: {baseline.UnitTests.Passed}/{baseline.UnitTests.TotalTests}");
            sb.AppendLine($"- Current unit tests: {testResults.UnitTests.Passed}/{testResults.UnitTests.TotalTests}");
            sb.AppendLine();
        }

        sb.AppendLine("**Task**: Evaluate if the changes achieved the intended improvement.");
        sb.AppendLine();
        sb.AppendLine("Consider:");
        sb.AppendLine("1. Did all tests pass?");
        sb.AppendLine("2. Is the code quality maintained or improved?");
        sb.AppendLine("3. Is the change safe to merge?");
        sb.AppendLine("4. Does it meet the user's original intent?");
        sb.AppendLine();
        sb.AppendLine("Format your response as valid JSON:");
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"decision\": \"accept|reject|defer\",");
        sb.AppendLine("  \"confidence\": 0.85,");
        sb.AppendLine("  \"improvementScore\": 7.5,");
        sb.AppendLine("  \"rationale\": \"Explanation of the decision\",");
        sb.AppendLine("  \"recommendations\": [\"suggestion 1\", \"suggestion 2\"]");
        sb.AppendLine("}");
        sb.AppendLine("```");

        return sb.ToString();
    }

    /// <summary>
    /// Génère un prompt pour analyser les métriques système.
    /// </summary>
    public static string AnalyzeMetrics(MetricsSnapshot metrics, MetricsSnapshot? baseline)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Analyze the following system metrics and identify potential improvement areas.");
        sb.AppendLine();
        sb.AppendLine("**Current Metrics**:");
        sb.AppendLine($"- CPU Usage: {metrics.CpuPercent:F1}%");
        sb.AppendLine($"- Memory Usage: {metrics.MemoryMb:F0} MB");
        if (metrics.ResponseTimeP95Ms > 0)
            sb.AppendLine($"- Response Time p95: {metrics.ResponseTimeP95Ms:F0} ms");
        if (metrics.ResponseTimeP99Ms > 0)
            sb.AppendLine($"- Response Time p99: {metrics.ResponseTimeP99Ms:F0} ms");
        if (metrics.Throughput > 0)
            sb.AppendLine($"- Throughput: {metrics.Throughput:F0} req/s");
        sb.AppendLine();

        if (baseline != null)
        {
            sb.AppendLine("**Baseline Comparison**:");
            sb.AppendLine($"- CPU Delta: {metrics.CpuPercent - baseline.CpuPercent:F1}%");
            sb.AppendLine($"- Memory Delta: {metrics.MemoryMb - baseline.MemoryMb:F0} MB");
            if (metrics.ResponseTimeP95Ms > 0 && baseline.ResponseTimeP95Ms > 0)
            {
                var delta = metrics.ResponseTimeP95Ms - baseline.ResponseTimeP95Ms;
                var percentChange = baseline.ResponseTimeP95Ms > 0
                    ? (delta / baseline.ResponseTimeP95Ms) * 100
                    : 0;
                sb.AppendLine($"- Response Time Delta: {delta:F0} ms ({percentChange:+F1}%)");
            }
            sb.AppendLine();
        }

        sb.AppendLine("**Task**: Identify metrics that may indicate performance issues.");
        sb.AppendLine("Report your findings as JSON.");

        return sb.ToString();
    }
}

/// <summary>
/// Snapshot des métriques système.
/// </summary>
public sealed record MetricsSnapshot
{
    public double CpuPercent { get; init; }
    public double MemoryMb { get; init; }
    public double ResponseTimeP95Ms { get; init; }
    public double ResponseTimeP99Ms { get; init; }
    public double Throughput { get; init; }
    public DateTimeOffset MeasuredAt { get; init; } = DateTimeOffset.UtcNow;
}