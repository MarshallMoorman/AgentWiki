# agent-wiki Architecture Overview

> **Offline / inventory-derived architecture** (no LLM credentials configured). Review before relying on it.

## Summary

agent-wiki is a C#, Markdown, JSON, YAML codebase with 89 tracked files (~9,728 lines of text). Inventory discovery used `Git`. This document was produced offline from repository analysis (no LLM call).

## System context

Primary languages: C#, Markdown, JSON, YAML. Category mix — Source: 52, Tests: 17, Config: 9, Docs: 11.

## Diagram

```mermaid
flowchart TB
    Root[agent-wiki]
    Root --> N0[src]
    Root --> N1[tests]
    Root --> N2[.github]
    Root --> N3[examples]
```

## Layers

| Layer | Responsibility | Key paths |
|-------|----------------|-----------|
| src | Primary application and library source | `src/` |
| tests | Automated tests | `tests/` |
| .github | GitHub workflows and community files | `.github/` |
| examples | Sample code | `examples/` |

## Key components

- **CommandSettingsBase.cs** (`src/AgentWiki.Cli/Commands/CommandSettingsBase.cs`): Source file (C#)
- **GenerateCommand.cs** (`src/AgentWiki.Cli/Commands/GenerateCommand.cs`): Source file (C#)
- **InitCommand.cs** (`src/AgentWiki.Cli/Commands/InitCommand.cs`): Source file (C#)
- **StatusCommand.cs** (`src/AgentWiki.Cli/Commands/StatusCommand.cs`): Source file (C#)
- **UpdateCommand.cs** (`src/AgentWiki.Cli/Commands/UpdateCommand.cs`): Source file (C#)
- **TypeRegistrar.cs** (`src/AgentWiki.Cli/Infrastructure/TypeRegistrar.cs`): Source file (C#)
- **Program.cs** (`src/AgentWiki.Cli/Program.cs`): Source file (C#)
- **AgentBootstrapper.cs** (`src/AgentWiki.Cli/Services/AgentBootstrapper.cs`): Source file (C#)
- **ArchitectureGenerator.cs** (`src/AgentWiki.Cli/Services/ArchitectureGenerator.cs`): Source file (C#)
- **ConfigLoader.cs** (`src/AgentWiki.Cli/Services/ConfigLoader.cs`): Source file (C#)
- **GitChangeDetector.cs** (`src/AgentWiki.Cli/Services/GitChangeDetector.cs`): Source file (C#)
- **GitProcess.cs** (`src/AgentWiki.Cli/Services/GitProcess.cs`): Source file (C#)
- **InitService.cs** (`src/AgentWiki.Cli/Services/InitService.cs`): Source file (C#)
- **LastRunStore.cs** (`src/AgentWiki.Cli/Services/LastRunStore.cs`): Source file (C#)
- **LlmResilience.cs** (`src/AgentWiki.Cli/Services/LlmResilience.cs`): Source file (C#)

## Important flows

1. Developer/agent runs CLI or build tooling against repository source.
2. Configuration (csproj/json/yml) drives project composition and runtime settings.
3. Tests exercise source modules under tests/ or *.Tests projects.

## Key decisions

- Prefer inventory-backed paths over invented module names.
- Treat generated wiki output under docs/wiki as derived artifacts.

## Gotchas

- Offline mode cannot infer runtime topology or domain rules—verify against source.
- Ignored paths (bin/obj/node_modules/docs/wiki) are intentionally excluded from analysis.

## How to extend / modify

- Add source under existing top-level folders to match observed layout.
- Configure Azure OpenAI / OpenAI credentials to upgrade this page to LLM-authored architecture.
- Adjust IgnorePatterns and MaxFilesToAnalyze in .agentwiki/config.json to refine inventory.

---

_Repository: `agent-wiki`_
