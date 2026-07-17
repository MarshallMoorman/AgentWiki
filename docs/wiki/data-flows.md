# Data Flows

> Important flows for agents implementing features or debugging.

## Architecture flows

1. User or automation invokes the application or workflow.
2. Configuration is loaded through `src/AgentWiki.App/Services/ConfigLoader.cs` and environment settings are resolved through `src/AgentWiki.App/Services/DotEnvLoader.cs`.
3. Repository state is inspected using git services including `GitChangeDetector` and `GitProcess`.
4. Generation services load prompt templates from `src/AgentWiki.App/Prompts/` and construct AI-generation requests.
5. Generators such as `ArchitectureGenerator` and `AgentsMdGenerator` create repository documentation artifacts.
6. Logging and telemetry are emitted through infrastructure components.
7. CI workflows in `.github/workflows/` execute validation and refresh automation around generated content.

## Module-oriented flow (recommended)

1. Read `architecture.md` for system boundaries.
2. Open the relevant module page under `modules/`.
3. Inspect entry points and related files listed on that page.
4. Check `cross-cutting/` for logging, config, and error-handling conventions.

## Module entry points

### AgentWiki Application

- `src/AgentWiki.App/ServiceCollectionExtensions.cs`
- `src/AgentWiki.App/Services/AgentBootstrapper.cs`
- `src/AgentWiki.App/Services/AgentsMdGenerator.cs`
- `src/AgentWiki.App/Services/ArchitectureGenerator.cs`

### Prompt Templates

- `src/AgentWiki.App/Prompts/SystemPrompt.txt`
- `src/AgentWiki.App/Prompts/ArchitectureOverviewPrompt.txt`
- `src/AgentWiki.App/Prompts/ModuleAnalysisPrompt.txt`
- `src/AgentWiki.App/Prompts/ModulePlanPrompt.txt`
- `src/AgentWiki.App/Prompts/CrossCuttingPrompt.txt`
- `src/AgentWiki.App/Prompts/CrossLinkValidationPrompt.txt`

### Infrastructure & Telemetry

- `src/AgentWiki.App/Infrastructure/AgentWikiLogging.cs`
- `src/AgentWiki.App/Infrastructure/ApplicationInsightsRunTelemetry.cs`
- `src/AgentWiki.App/Services/DotEnvLoader.cs`
- `src/AgentWiki.App/Services/GitProcess.cs`

### Automated Tests

- `tests/AgentWiki.Cli.Tests/AgentWiki.Cli.Tests.csproj`
- `tests/AgentWiki.Desktop.Tests/AgentWiki.Desktop.Tests.csproj`
- `tests/AgentWiki.Cli.Tests/Integration/EndToEndOfflineTests.cs`

### Documentation & Product Specification

- `README.md`
- `AGENTS.md`
- `AgentWiki-Project-Specification.md`

### Examples & Configuration

- `examples/agentwiki.config.json`
- `examples/github-actions/agent-wiki-update.yml`
- `examples/azure-pipelines/agent-wiki-update.yml`

### Automation, Packaging & CI

- `.github/workflows/ci.yml`
- `.github/workflows/wiki-refresh.yml`
- `scripts/pack-and-install-tool.sh`
- `scripts/run-desktop.sh`
- `.grok/skills/bump-version/SKILL.md`
- `.grok/skills/bump-version/scripts/bump-version.sh`

