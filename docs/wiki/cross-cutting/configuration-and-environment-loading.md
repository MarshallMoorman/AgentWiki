# Configuration and Environment Loading

> Current cross-cutting documentation (AI-assisted).

## Summary

Application behavior is driven by configuration files, environment variables, and optional .env loading. Configuration is validated and merged before services execute.

## Patterns

- Centralized configuration loading
- Environment variable overrides
- Example configuration templates
- Startup validation of settings

## Key files

- `src/AgentWiki.App/Services/ConfigLoader.cs`
- `src/AgentWiki.App/Services/DotEnvLoader.cs`
- `examples/agentwiki.config.json`
- `README.md`

## Guidance for agents

- Use ConfigLoader as the primary entry point for new settings.
- Add new configuration fields to example configuration files and documentation.
- Preserve environment-variable support when introducing configurable behavior.
- Validate required settings early during startup rather than deep in execution flows.

## Navigation

- [Wiki index](../index.md)
- [Architecture](../architecture.md)
