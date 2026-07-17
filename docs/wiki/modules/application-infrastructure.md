# Infrastructure & Telemetry

> Current module documentation for coding agents (AI-assisted).

## Purpose

Provides cross-cutting application infrastructure for logging, telemetry, environment-variable loading, and Git process execution used by the application.

## Entry points

- `src/AgentWiki.App/Infrastructure/AgentWikiLogging.cs`
- `src/AgentWiki.App/Infrastructure/ApplicationInsightsRunTelemetry.cs`
- `src/AgentWiki.App/Services/DotEnvLoader.cs`
- `src/AgentWiki.App/Services/GitProcess.cs`

## Dependencies / roots

- `Application Insights telemetry integration (via src/AgentWiki.App/Infrastructure/ApplicationInsightsRunTelemetry.cs)`
- `Logging infrastructure (via src/AgentWiki.App/Infrastructure/AgentWikiLogging.cs)`
- `Environment-variable loading (via src/AgentWiki.App/Services/DotEnvLoader.cs)`
- `Git process execution (via src/AgentWiki.App/Services/GitProcess.cs)`

## Key types / files

- AgentWikiLogging
- ApplicationInsightsRunTelemetry
- DotEnvLoader
- GitProcess

## Endpoints / Public API

_No HTTP or Function endpoints discovered for this module._

## How to extend

- Add new logging behavior in src/AgentWiki.App/Infrastructure/AgentWikiLogging.cs so application-wide logging remains centralized.
- Add telemetry collection or reporting logic in src/AgentWiki.App/Infrastructure/ApplicationInsightsRunTelemetry.cs to keep operational instrumentation in one location.
- Extend environment loading behavior in src/AgentWiki.App/Services/DotEnvLoader.cs rather than duplicating configuration-loading logic elsewhere.
- Add Git-related process operations in src/AgentWiki.App/Services/GitProcess.cs to preserve a single abstraction for invoking Git.
- Keep infrastructure concerns isolated in the Infrastructure and Services files listed above so application features consume shared services instead of creating parallel implementations.

## Gotchas

- These files provide shared infrastructure concerns; changes can affect multiple application features.
- Keep logging and telemetry behavior coordinated so operational data remains consistent across the application.
- Environment-loading changes can alter runtime configuration behavior for all consumers.
- Git process execution changes can affect any workflow that depends on repository operations.
- Prefer extending the existing infrastructure files instead of introducing duplicate logging, telemetry, environment, or Git execution paths.

## Related files

- `src/AgentWiki.App/Infrastructure/AgentWikiLogging.cs`
- `src/AgentWiki.App/Infrastructure/ApplicationInsightsRunTelemetry.cs`
- `src/AgentWiki.App/Services/DotEnvLoader.cs`
- `src/AgentWiki.App/Services/GitProcess.cs`

## Navigation

- [Wiki index](../index.md)
- [Architecture](../architecture.md)
- [API Endpoints](../api-endpoints.md)
