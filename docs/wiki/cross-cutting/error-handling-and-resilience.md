# Error Handling and Resilience

> Current cross-cutting documentation (AI-assisted).

## Summary

The application performs external operations such as file access, Git execution, configuration loading, and AI interactions, requiring consistent failure handling and diagnostics.

## Patterns

- Startup validation
- External-process error handling
- Configuration safety checks
- Operational logging around failures

## Key files

- `src/AgentWiki.App/Services/ConfigLoader.cs`
- `src/AgentWiki.App/Services/GitProcess.cs`
- `src/AgentWiki.App/Services/AgentBootstrapper.cs`
- `src/AgentWiki.App/Infrastructure/AgentWikiLogging.cs`

## Guidance for agents

- Validate inputs before expensive repository or generation operations.
- Surface actionable error messages that identify failing files, commands, or settings.
- Log exceptions with sufficient context for troubleshooting.
- Avoid swallowing failures from external dependencies without recording diagnostics.

## Navigation

- [Wiki index](../index.md)
- [Architecture](../architecture.md)
