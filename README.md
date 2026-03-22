# AutoLoop

AutoLoop is an autonomous code self-improvement framework written in C# (.NET 8). It uses the [Claude Code](https://docs.anthropic.com/en/docs/claude-code) CLI as its AI engine to continuously analyze a target codebase, propose improvements, validate them with tests, and merge accepted changes via GitHub — all without human intervention.

---

## How it works

AutoLoop runs as a background service that executes a closed 4-phase loop on a configurable interval:

```
┌─────────────────────────────────────────────────────────────┐
│                      AUTOLOOP CYCLE                         │
│                                                             │
│  ┌───────────────┐    ┌───────────────┐                     │
│  │  Phase 1      │    │  Phase 2      │                     │
│  │  Hypothesis   │───▶│  Mutation     │                     │
│  │  Generation   │    │  Application  │                     │
│  └───────────────┘    └───────────────┘                     │
│         ▲                     │                             │
│         │                     ▼                             │
│  ┌───────────────┐    ┌───────────────┐                     │
│  │  Phase 4      │    │  Phase 3      │                     │
│  │  Evaluation   │◀───│  Testing      │                     │
│  │  & Decision   │    │  (unit/perf/  │                     │
│  └───────────────┘    │   regression) │                     │
│         │             └───────────────┘                     │
│         ▼                                                   │
│   Accept → PR merge       Reject/Fail → Rollback            │
└─────────────────────────────────────────────────────────────┘
```

**Phase 1 — Hypothesis Generation**: Claude Code analyzes the target project and the user's stated intent to propose a ranked list of concrete improvement hypotheses (e.g., "Replace string concatenation with StringBuilder in the hot path of MessageProcessor.cs").

**Phase 2 — Mutation**: Claude Code applies the top hypothesis as a code change. A Git branch (`auto-loop/cycle-<id>`) is created beforehand to isolate the change.

**Phase 3 — Testing**: A composite test runner executes unit tests, performance benchmarks, and regression checks against the previous baseline. If any test fails, an immediate rollback is triggered.

**Phase 4 — Evaluation & Decision**: Claude Code evaluates the test results against the baseline. Statistical significance is assessed (Welch t-test, Mann-Whitney U, Cohen's d, Bootstrap CI). The engine decides to **Accept** (merge PR), **Reject** (rollback), or **Defer** (keep branch for review).

---

## Capabilities

### Multi-language project support

AutoLoop auto-detects the target project type and adapts test execution accordingly:

| Language | Detection | Test command |
|---|---|---|
| .NET (C#/F#/VB) | `*.csproj` | `dotnet test` |
| Node.js | `package.json` | `npm test` / `yarn test` / `pnpm test` |
| Python | `pyproject.toml`, `requirements.txt` | `pytest` |
| Go | `go.mod` | `go test ./...` |
| Rust | `Cargo.toml` | `cargo test` |
| Java | `pom.xml`, `build.gradle` | `mvn test` / `gradle test` |
| Ruby | `Gemfile` | `bundle exec rspec` |
| PHP | `composer.json` | `vendor/bin/phpunit` / `vendor/bin/pest` |

### GitHub versioning backend

Each cycle creates an isolated branch, commits the mutation, opens a Pull Request, and auto-merges it if the evaluation passes. GitHub operations are resilient via Polly (3 exponential retries + circuit breaker).

### 3-tier rollback

If a mutation fails validation, rollback is triggered automatically:

1. **Tier 1 — In-memory restore**: rewrites the file from the captured original content (fastest, no git required)
2. **Tier 2 — Git checkout**: restores the file from HEAD if no commit exists yet
3. **Tier 3 — Git revert**: creates a revert commit to undo a merged change while preserving history

### Statistical evaluation

Test performance is compared against a stored baseline using:
- **Welch t-test** — parametric significance test (unequal variances)
- **Mann-Whitney U** — non-parametric significance test
- **Cohen's d** — effect size measurement (threshold: d ≥ 0.2)
- **Bootstrap CI** — confidence interval via 10,000 resamples

A change is only accepted if it is statistically significant, passes all unit tests, and meets the configured minimum improvement threshold (default: 5%).

### Observability

- **Prometheus metrics** exposed on port `9090` (cycle counts, phase durations, decision outcomes, rollback rates, Claude Code call stats)
- **Serilog** structured JSON logging with daily rolling files
- **AuditTrail** — hash-chained append-only JSONL file for tamper-evident cycle history
- **CycleJournal** — full per-cycle record (hypotheses, mutations, test results, decisions)
- **CycleMemory** — persisted summary of recent cycles, fed back into each new hypothesis prompt

### Mutation strategies (for .NET targets)

For .NET projects, AutoLoop includes three Roslyn-backed code mutation strategies used as fallback or supplementary to Claude Code:
- Documentation refactoring
- LINQ optimization
- Cache introduction

---

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code) installed and authenticated (`claude` on PATH)
- A GitHub repository with a personal access token (`GITHUB_TOKEN` env var)
- Git repository initialized in the target project

---

## Quick start

### 1. Clone and build

```bash
git clone <repo-url>
cd auto_code_loop
dotnet build AutoLoop.sln
```

### 2. Configure

Edit `appsettings.json` and set your GitHub repository:

```json
"GitHub": {
  "Owner": "your-github-username",
  "Repository": "your-target-repo",
  "DefaultBranch": "main"
}
```

Set your GitHub token as an environment variable:

```bash
export GITHUB_TOKEN=ghp_your_token_here
```

### 3. Run in dry-run mode first

Dry-run executes Phase 1 (hypothesis generation) only — no files are modified, no branches created:

```bash
dotnet run --project src/AutoLoop.CLI -- "reduce memory usage in API handlers" --dry-run
```

### 4. Run for real

```bash
dotnet run --project src/AutoLoop.CLI -- "improve test coverage" --project-path /path/to/your/project
```

AutoLoop will loop indefinitely. Use `--max-cycles` to limit runs:

```bash
dotnet run --project src/AutoLoop.CLI -- "optimize database queries" --max-cycles 5
```

---

## CLI reference

```
autoloop <intent> [options]

Arguments:
  <intent>          Improvement goal in plain English
                    e.g. "reduce memory usage in API handlers"
                         "optimize database queries"
                         "improve test coverage"

Options:
  --dry-run         Phase 1 only — no code changes (default: false)
  --max-cycles <n>  Stop after n cycles (default: unlimited)
  --interactive     Prompt for confirmation before each mutation
  --project-path    Path to the target project (default: current directory)
  --config          Path to appsettings.json (default: appsettings.json)
```

---

## Configuration reference

Key sections of `appsettings.json`:

```json
{
  "Cycle": {
    "CycleIntervalMs": 60000,        // Delay between cycles in ms
    "MaxHypothesesPerCycle": 3,      // Hypotheses generated per cycle
    "DryRun": false,
    "TargetProjectPath": ".",
    "MaxCycles": null,               // null = infinite
    "InteractiveMode": false
  },
  "ClaudeCode": {
    "Executable": "claude",          // Claude Code CLI binary
    "DefaultModel": "claude-sonnet-4-6",
    "MaxTokens": 4096,
    "TimeoutMs": 300000,             // 5 minutes per call
    "Temperature": 0.7
  },
  "Evaluation": {
    "MinPerformanceImprovementPercent": 5.0,
    "MaxAllowedRegressionPercent": 1.0,
    "StatisticalSignificanceAlpha": 0.05,
    "MinCohensD": 0.2,
    "RequireBootstrapCIPositive": true,
    "RequireAllUnitTestsPassing": true
  },
  "Monitoring": {
    "PrometheusPort": 9090,
    "MaxConsecutiveFailures": 3      // Alert threshold
  },
  "Storage": {
    "JournalsPath": "./storage/journals",
    "AuditTrailPath": "./storage/audit.jsonl",
    "BaselinePath": "./storage/baseline.json",
    "MemoryPath": "./storage/memory"
  }
}
```

For local development, override settings in `appsettings.Development.json` (DryRun and Debug logging are enabled by default in this file).

---

## Project structure

```
auto_code_loop/
├── src/
│   ├── AutoLoop.CLI/             Entry point, dependency injection wiring
│   ├── AutoLoop.Core/            Cycle orchestrator, interfaces, models, prompts
│   ├── AutoLoop.ClaudeCode/      Claude Code CLI executor, cycle memory, intent preserver
│   ├── AutoLoop.Hypothesis/      Hypothesis engine and ranker
│   ├── AutoLoop.Mutation/        Mutation engine, Roslyn strategies, change tracker
│   ├── AutoLoop.Testing/         Composite test runner (unit/perf/regression), multi-language
│   ├── AutoLoop.Evaluation/      Evaluation engine, decision engine, statistical tests
│   ├── AutoLoop.Versioning/      GitHub backend (Octokit.NET), local git (LibGit2Sharp)
│   ├── AutoLoop.Rollback/        3-tier rollback manager
│   ├── AutoLoop.Logging/         Serilog setup, audit trail, cycle journal, event bus
│   ├── AutoLoop.Monitoring/      Prometheus metrics registry, system monitor, alert manager
│   └── AutoLoop.ProjectDetection/ Multi-language project and test framework detection
└── tests/
    └── AutoLoop.Tests/           Unit tests (xUnit + FluentAssertions)
```

---

## Storage layout

AutoLoop writes runtime data to the `./storage/` directory (gitignored):

| Path | Content |
|---|---|
| `storage/journals/` | Per-cycle JSON records |
| `storage/audit.jsonl` | Hash-chained tamper-evident audit trail |
| `storage/baseline.json` | Latest accepted test suite baseline |
| `storage/changes.jsonl` | Append-only change history |
| `storage/memory/` | Cycle summaries for cross-cycle context |
| `logs/` | Daily rolling Serilog JSON log files |

---

## License

MIT
