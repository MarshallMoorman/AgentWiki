# Data Flows

> Important flows for agents implementing features or debugging.

## Architecture flows

1. Developer/agent runs CLI or build tooling against repository source.
2. Configuration (csproj/json/yml) drives project composition and runtime settings.
3. Tests exercise source modules under tests/ or *.Tests projects.

## Module-oriented flow (recommended)

1. Read `architecture.md` for system boundaries.
2. Open the relevant module page under `modules/`.
3. Inspect entry points and related files listed on that page.
4. Check `cross-cutting/` for logging, config, and error-handling conventions.

## Module entry points

### AgentWiki.Cli

- `src/AgentWiki.Cli/Commands/CommandSettingsBase.cs`
- `src/AgentWiki.Cli/Commands/GenerateCommand.cs`
- `src/AgentWiki.Cli/Commands/InitCommand.cs`
- `src/AgentWiki.Cli/Commands/StatusCommand.cs`
- `src/AgentWiki.Cli/Commands/TestProviderCommand.cs`
- `src/AgentWiki.Cli/Commands/UpdateCommand.cs`
- `src/AgentWiki.Cli/Program.cs`

### AgentWiki.Core

_No explicit entry points listed._

### AgentWiki.Cli.Tests

_No explicit entry points listed._

