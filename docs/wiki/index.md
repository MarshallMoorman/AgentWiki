# agent-wiki — AgentWiki

> Agent-optimized documentation generated from the current repository inventory (no live LLM for this run).

## Navigation

| Page | Description |
|------|-------------|
| [Architecture](architecture.md) | System design, layers, decisions |
| [Key Components](key-components.md) | Component map |
| [Data Flows](data-flows.md) | Important request/process flows |
| [Repository Inventory](inventory.md) | File inventory summary |
| [Glossary](glossary.md) | Terms and abbreviations |
| [Getting Started](getting-started.md) | Agent usage guide |

### Modules

| Module | Purpose |
|--------|---------|
| [AgentWiki.Cli](modules/agentwiki-cli.md) | Project module defined by `src/AgentWiki.Cli/AgentWiki.Cli.csproj`. |
| [AgentWiki.Core](modules/agentwiki-core.md) | Project module defined by `src/AgentWiki.Core/AgentWiki.Core.csproj`. |
| [AgentWiki.Cli.Tests](modules/agentwiki-cli-tests.md) | Project module defined by `tests/AgentWiki.Cli.Tests/AgentWiki.Cli.Tests.csproj`. |

### Cross-cutting

| Topic | Summary |
|-------|---------|
| [Configuration](cross-cutting/configuration.md) | Configuration, project settings, Policies/ (APIM), and pipeline definitions from the inventory. |
| [Logging and Telemetry](cross-cutting/logging-and-telemetry.md) | Logging/telemetry-related files and conventions inferred from inventory. |
| [Error Handling](cross-cutting/error-handling.md) | Error-handling patterns inferred from naming and result types in the inventory. |
| [Testing](cross-cutting/testing.md) | Test projects and files discovered during analysis. |

## Quick facts

- **Repository:** `agent-wiki`
- **Generated at (UTC):** 2026-07-10T14:18:51.9237040+00:00
- **Files (after ignores):** 107
- **Selected for analysis:** 107
- **Approx. lines:** 12,465
- **Modules documented:** 3
- **Generation mode:** inventory-based
- **Correlation ID:** `3f48e2e867844aeda1322879c9e6fdb0`

## How to use this wiki

1. Read [architecture.md](architecture.md) for the current system shape.
2. Open the relevant page under [modules/](modules/) or [cross-cutting/](cross-cutting/).
3. Use [inventory.md](inventory.md) when you need exact paths.
4. Treat this as a map of the live tree; confirm critical details in source when implementing.
