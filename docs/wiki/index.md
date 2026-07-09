# agent-wiki — AgentWiki

> **Agent-optimized documentation** (offline multi-step generation). Review before relying on it.

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
| [Logging and Telemetry](cross-cutting/logging-and-telemetry.md) | Logging/telemetry-related files and conventions inferred from inventory. No strongly matching files… |
| [Error Handling](cross-cutting/error-handling.md) | Error-handling patterns inferred from naming and result types in the inventory. |
| [Testing](cross-cutting/testing.md) | Test projects and files discovered during analysis. |

## Quick facts

- **Repository:** `agent-wiki`
- **Generated at (UTC):** 2026-07-09T21:30:19.7648120+00:00
- **Files (after ignores):** 81
- **Selected for analysis:** 81
- **Approx. lines:** 8,943
- **Modules documented:** 3
- **Architecture source:** offline
- **Correlation ID:** `4d172c33cdb0477ab957b10ca40d2d64`

## How to use this wiki

1. Read [architecture.md](architecture.md) first.
2. Drill into relevant [modules](modules/) and [cross-cutting](cross-cutting/) pages.
3. Use [inventory.md](inventory.md) for concrete paths.
4. Verify AI-generated guidance against source before large changes.
