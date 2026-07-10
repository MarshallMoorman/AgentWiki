# Logging and Telemetry

> Cross-cutting notes derived from the current file inventory.

## Summary

Logging/telemetry-related files and conventions inferred from inventory.

## Patterns

- Use structured logging with correlation IDs for multi-step runs.
- Never log secrets, API keys, or full prompt/response payloads by default.

## Key files

- `src/AgentWiki.Cli/Infrastructure/AgentWikiLogging.cs`

## Guidance for agents

- Add log events around external calls and generation pipeline steps.
- Prefer warning/error levels for actionable failures.

## Navigation

- [Wiki index](../index.md)
- [Architecture](../architecture.md)
