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
    /// Génère le prompt pour la Phase 2: Application de mutation (mode agentique).
    /// Claude doit modifier les fichiers directement via ses outils Edit/Write — pas de JSON.
    /// </summary>
    public static string ApplyMutation(Hypothesis hypothesis, ProjectInfo project)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"You are improving a {project.Type} project written in {project.Language ?? "an unknown language"}.");
        sb.AppendLine();
        sb.AppendLine("**Improvement to implement**:");
        sb.AppendLine($"Description: {hypothesis.Rationale}");
        sb.AppendLine($"Type: {hypothesis.Type}");
        sb.AppendLine($"Target file: {hypothesis.TargetFile}");
        if (!string.IsNullOrEmpty(hypothesis.TargetMethod))
            sb.AppendLine($"Target method: {hypothesis.TargetMethod}");
        sb.AppendLine($"Expected impact: {hypothesis.ExpectedImpact}/10");
        sb.AppendLine();
        sb.AppendLine("**Project context**:");
        sb.AppendLine($"- Path: {project.ProjectPath}");
        if (!string.IsNullOrEmpty(project.Framework))
            sb.AppendLine($"- Framework: {project.Framework}");
        if (project.SourcePatterns.Count > 0)
            sb.AppendLine($"- Source patterns: {string.Join(", ", project.SourcePatterns)}");
        sb.AppendLine();
        sb.AppendLine("**Instructions**:");
        sb.AppendLine("1. Read the target file and understand the existing code.");
        sb.AppendLine("2. Apply the improvement described above using your Edit or Write tools.");
        sb.AppendLine("3. Make minimal, focused changes — preserve all existing functionality.");
        sb.AppendLine("4. Follow the project's existing code style and conventions.");
        sb.AppendLine("5. If necessary, read related files to understand context before editing.");
        sb.AppendLine();
        sb.AppendLine("Do NOT return JSON. Use your Edit/Write tools to modify the file(s) directly on disk.");
        sb.AppendLine("After applying the changes, briefly summarize what you modified (1-3 lines).");

        return sb.ToString();
    }

    /// <summary>
    /// Génère le prompt pour la Phase 3a : Génération de tests ciblés sur la mutation (mode agentique).
    /// Claude doit explorer les conventions de test du projet et écrire des tests directement sur disque.
    /// </summary>
    public static string GenerateTargetedTests(
        ChangeRecord appliedChange,
        ProjectInfo project,
        string hypothesisRationale)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"You are writing targeted tests for a {project.Type} project written in {project.Language ?? "an unknown language"}.");
        sb.AppendLine();
        sb.AppendLine("**Code change that was just applied**:");
        sb.AppendLine($"- Modified file: {appliedChange.FilePath}");
        sb.AppendLine($"- Rationale: {hypothesisRationale}");
        sb.AppendLine($"- Mutation type: {appliedChange.MutationType}");
        if (!string.IsNullOrWhiteSpace(appliedChange.UnifiedDiff))
        {
            sb.AppendLine();
            sb.AppendLine("**Unified diff of the change**:");
            sb.AppendLine("```diff");
            // Limit diff length to avoid over-sized prompts
            var diff = appliedChange.UnifiedDiff;
            sb.AppendLine(diff.Length > 3000 ? diff[..3000] + "\n... (truncated)" : diff);
            sb.AppendLine("```");
        }
        sb.AppendLine();
        sb.AppendLine("**Project context**:");
        sb.AppendLine($"- Path: {project.ProjectPath}");
        if (!string.IsNullOrEmpty(project.TestCommand))
            sb.AppendLine($"- Test command: {project.TestCommand}");
        if (!string.IsNullOrEmpty(project.Framework))
            sb.AppendLine($"- Framework: {project.Framework}");
        sb.AppendLine();
        sb.AppendLine("**Instructions**:");
        sb.AppendLine("1. Explore the project's existing test files to understand conventions (naming, structure, assertion style).");
        sb.AppendLine("2. Read the modified file to understand the changed code.");
        sb.AppendLine("3. Write 2-5 targeted unit tests that specifically exercise the modified code.");
        sb.AppendLine("4. Place the test file following the project's existing test structure.");
        sb.AppendLine("5. Ensure tests use the project's existing test framework and assertion library.");
        sb.AppendLine("6. Tests should cover: happy path, edge cases, and regression for the modified logic.");
        sb.AppendLine();
        sb.AppendLine("Do NOT return JSON. Write the test file(s) directly to disk using your Write or Edit tools.");
        sb.AppendLine("After writing, list each created/modified file prefixed with 'CREATED: ' or 'MODIFIED: '.");

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