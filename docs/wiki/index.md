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
| [Configuration](cross-cutting/configuration.md) | Configuration files and project settings discovered in the inventory. |
| [Logging and Telemetry](cross-cutting/logging-and-telemetry.md) | Logging/telemetry-related files and conventions inferred from inventory. |
| [Error Handling](cross-cutting/error-handling.md) | Error-handling patterns inferred from naming and result types in the inventory. |
| [Testing](cross-cutting/testing.md) | Test projects and files discovered during analysis. |

## Quick facts

- **Repository:** `agent-wiki`
- **Generated at (UTC):** 2026-07-10T13:48:28.6747920+00:00
- **Files (after ignores):** 102
- **Selected for analysis:** 102
- **Approx. lines:** 11,506
- **Modules documented:** 3
- **Generation mode:** inventory-based
- **Correlation ID:** `6276aeee38b04d57a6fcde36a83b68e0`

## How to use this wiki

1. Read [architecture.md](architecture.md) for the current system shape.
2. Open the relevant page under [modules/](modules/) or [cross-cutting/](cross-cutting/).
3. Use [inventory.md](inventory.md) when you need exact paths.
4. Treat this as a map of the live tree; confirm critical details in source when implementing.
