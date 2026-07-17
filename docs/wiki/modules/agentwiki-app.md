# AgentWiki Application

> Current module documentation for coding agents (AI-assisted).

## Purpose

Primary application project that coordinates repository analysis, configuration loading, git change detection, AI prompt execution, dependency injection setup, and generation of agent-oriented wiki/documentation outputs.

## Entry points

- `src/AgentWiki.App/ServiceCollectionExtensions.cs`
- `src/AgentWiki.App/Services/AgentBootstrapper.cs`
- `src/AgentWiki.App/Services/AgentsMdGenerator.cs`
- `src/AgentWiki.App/Services/ArchitectureGenerator.cs`

## Dependencies / roots

- `src/AgentWiki.App/AgentWiki.App.csproj`

## Key types / files

- ServiceCollectionExtensions
- AgentBootstrapper
- AgentsMdGenerator
- ArchitectureGenerator
- ConfigLoader
- GitChangeDetector

## Endpoints / Public API

_No HTTP or Function endpoints discovered for this module._

## How to extend

- Register new application services through the dependency-injection configuration in src/AgentWiki.App/ServiceCollectionExtensions.cs.
- Add startup or orchestration behavior in src/AgentWiki.App/Services/AgentBootstrapper.cs when introducing new generation workflows.
- Extend documentation output generation by updating src/AgentWiki.App/Services/AgentsMdGenerator.cs or src/AgentWiki.App/Services/ArchitectureGenerator.cs as appropriate.
- Add new configuration sources or settings handling in src/AgentWiki.App/Services/ConfigLoader.cs rather than bypassing the existing configuration-loading path.
- Integrate repository-change-aware behavior through src/AgentWiki.App/Services/GitChangeDetector.cs so generation logic remains aligned with detected git state.
- Update project references and package dependencies in src/AgentWiki.App/AgentWiki.App.csproj when introducing new external dependencies.

## Gotchas

- Keep service registrations and consuming components synchronized; new services should be added through the central DI setup in src/AgentWiki.App/ServiceCollectionExtensions.cs.
- Configuration-related changes should flow through src/AgentWiki.App/Services/ConfigLoader.cs to avoid inconsistent settings resolution.
- Generation features may depend on orchestration performed by src/AgentWiki.App/Services/AgentBootstrapper.cs; review startup flow before introducing parallel execution paths.
- Changes to git-aware behavior should consider interactions with src/AgentWiki.App/Services/GitChangeDetector.cs and downstream generation components.
- This module acts as an orchestration layer; review impacts across configuration, git integration, and generators before making cross-cutting changes.

## Related files

- `src/AgentWiki.App/AgentWiki.App.csproj`
- `src/AgentWiki.App/ServiceCollectionExtensions.cs`
- `src/AgentWiki.App/Services/AgentBootstrapper.cs`
- `src/AgentWiki.App/Services/AgentsMdGenerator.cs`
- `src/AgentWiki.App/Services/ArchitectureGenerator.cs`
- `src/AgentWiki.App/Services/ConfigLoader.cs`
- `src/AgentWiki.App/Services/GitChangeDetector.cs`

## Navigation

- [Wiki index](../index.md)
- [Architecture](../architecture.md)
- [API Endpoints](../api-endpoints.md)
