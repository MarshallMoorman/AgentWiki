# Key Components

> Combines architecture components, module map, public API endpoints, and inventory.

## Architecture components

- **AgentBootstrapper** (`src/AgentWiki.App/Services/AgentBootstrapper.cs`): Builds and coordinates AI-agent execution and generation workflows.
- **AgentsMdGenerator** (`src/AgentWiki.App/Services/AgentsMdGenerator.cs`): Generates AGENTS.md-style repository documentation.
- **ArchitectureGenerator** (`src/AgentWiki.App/Services/ArchitectureGenerator.cs`): Produces architecture-focused documentation from repository analysis.
- **ConfigLoader** (`src/AgentWiki.App/Services/ConfigLoader.cs`): Loads, validates, and resolves application configuration.
- **DotEnvLoader** (`src/AgentWiki.App/Services/DotEnvLoader.cs`): Imports environment variables from dotenv-style sources.
- **GitChangeDetector** (`src/AgentWiki.App/Services/GitChangeDetector.cs`): Determines repository changes and supports incremental analysis workflows.
- **GitProcess** (`src/AgentWiki.App/Services/GitProcess.cs`): Executes git operations used by repository analysis services.
- **AgentWikiLogging** (`src/AgentWiki.App/Infrastructure/AgentWikiLogging.cs`): Configures application logging behavior.
- **ApplicationInsightsRunTelemetry** (`src/AgentWiki.App/Infrastructure/ApplicationInsightsRunTelemetry.cs`): Captures execution telemetry and run-level observability.
- **ServiceCollectionExtensions** (`src/AgentWiki.App/ServiceCollectionExtensions.cs`): Registers application services and dependency injection wiring.
- **Prompt Templates** (`src/AgentWiki.App/Prompts/`): Stores reusable prompt definitions used by document generation services.

## Modules

- [AgentWiki Application](modules/agentwiki-app.md) — Primary application project that coordinates repository analysis, configuration loading, git change detection, AI prompt execution, dependency injection setup, and generation of agent-oriented wiki/documentation outputs.
- [Prompt Templates](modules/prompt-library.md) — Catalog of text prompt templates used by the application to instruct LLMs for architecture analysis, module discovery and analysis, implementation planning, cross-cutting concern analysis, cross-link validation, and wiki content generation.
- [Infrastructure & Telemetry](modules/application-infrastructure.md) — Provides cross-cutting application infrastructure for logging, telemetry, environment-variable loading, and Git process execution used by the application.
- [Automated Tests](modules/test-suite.md) — Provides repository-wide test coverage for CLI generation workflows, analysis utilities, orchestration services, desktop application services, view models, configuration handling, resilience behavior, rendering logic, and end-to-end offline wiki generation scenarios. The test projects act as executable specifications for expected behavior and are the primary safety net when modifying generation, analysis, service, or UI logic.
- [Documentation & Product Specification](modules/documentation-specification.md) — Human-authored project documentation that defines product goals, requirements, architecture direction, development workflows, agent-oriented guidance, handoff information, and planned future work for the Agent Wiki repository.
- [Examples & Configuration](modules/examples-and-config.md) — Provides reference configuration and CI/CD workflow examples that show how to configure and run AgentWiki from external repositories and automation pipelines.
- [Automation, Packaging & CI](modules/automation-and-ci.md) — Provides repository automation for continuous integration, tool packaging/installation, desktop execution, version-bump workflows, and wiki refresh operations through GitHub Actions and supporting scripts.

## Languages

| Language | Files |
|----------|------:|
| C# | 152 |
| Markdown | 10 |
| YAML | 4 |
| Shell | 3 |
| JSON | 3 |

