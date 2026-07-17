# Logging and Telemetry

> Current cross-cutting documentation (AI-assisted).

## Summary

Operational visibility is provided through centralized logging and optional telemetry collection for application runs.

## Patterns

- Central logging setup
- Run-level telemetry tracking
- Dependency-injected observability services
- Structured operational diagnostics

## Key files

- `src/AgentWiki.App/Infrastructure/AgentWikiLogging.cs`
- `src/AgentWiki.App/Infrastructure/ApplicationInsightsRunTelemetry.cs`
- `src/AgentWiki.App/ServiceCollectionExtensions.cs`

## Guidance for agents

- Use the existing logging infrastructure instead of writing directly to console where possible.
- Emit useful contextual information around long-running generation workflows.
- Ensure new background or generation services participate in telemetry and logging pipelines.
- Avoid introducing parallel observability mechanisms outside the established infrastructure.

## Navigation

- [Wiki index](../index.md)
- [Architecture](../architecture.md)
