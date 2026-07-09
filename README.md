# AgentWiki

**AgentWiki** (`agent-wiki`) is a native .NET 10 CLI that generates and maintains **agent-optimized documentation wikis** for codebases — a Microsoft/.NET alternative to LangChain OpenWiki.

> **Status:** Phase 2 complete — repository analysis with `.gitignore` support, inventory stats, and inventory-backed placeholder wiki.

## Quick start

```bash
# From repo root
dotnet build
dotnet run --project src/AgentWiki.Cli -- --help

# Scaffold config in a target repo
dotnet run --project src/AgentWiki.Cli -- init --repo-path /path/to/repo

# Generate placeholder wiki (Phase 1 — no LLM yet)
dotnet run --project src/AgentWiki.Cli -- generate --repo-path /path/to/repo --force

# Show status
dotnet run --project src/AgentWiki.Cli -- status --repo-path /path/to/repo
```

### Install as a local dotnet tool (optional)

```bash
dotnet pack src/AgentWiki.Cli -c Release -o ./artifacts
dotnet tool install --global --add-source ./artifacts AgentWiki.Cli
agent-wiki --help
```

## Commands

| Command | Description |
|---------|-------------|
| `agent-wiki init` | Create `.agentwiki/config.json`, sample prompts, `.env.example` |
| `agent-wiki generate` | Full wiki generation (Phase 1: placeholder Markdown) |
| `agent-wiki update` | Incremental update (Phase 1: same placeholder; git-aware in Phase 5) |
| `agent-wiki status` | Show config + last-run metadata |

### Common options

- `-r|--repo-path` — repository root (default: `.`)
- `-o|--output` — wiki output path (default: `docs/wiki`)
- `-c|--config` — path to config JSON
- `-m|--model` — model / deployment name
- `--provider` — `azure-openai` \| `openai` \| `github-models`
- `--force` — overwrite without confirmation
- `--dry-run` — do not write files
- `-v|--verbose` — verbose logging

## Configuration priority

1. CLI arguments (highest)
2. `.agentwiki/config.json` in the repo
3. Environment variables (`AGENTWIKI_*`)
4. `appsettings.json` (tool defaults)

## Solution layout

```
AgentWiki/
├── src/
│   ├── AgentWiki.Cli/     # Spectre.Console CLI + services
│   └── AgentWiki.Core/    # Models + abstractions
├── tests/
│   └── AgentWiki.Cli.Tests/
├── Directory.Build.props
└── AgentWiki.slnx
```

## Implementation roadmap

| Phase | Focus | Status |
|-------|--------|--------|
| 1 | Foundation + CLI skeleton | ✅ |
| 2 | RepoAnalyzer + gitignore | ✅ |
| 3 | Semantic Kernel + basic generation | ⏳ |
| 4 | Multi-step orchestrator + AGENTS.md | ⏳ |
| 5 | Incremental updates | ⏳ |
| 6 | Polish, CI, docs, tests | ⏳ |

### Phase 2 analysis features

- Discovers files via `git ls-files` when available, otherwise filesystem walk
- Honors nested `.gitignore` + `IgnorePatterns` from config
- Categorizes files (SourceCode, Tests, Configuration, Documentation, Diagrams, Other)
- Detects languages, counts lines (text files), builds top-folder stats
- Selects up to `MaxFilesToAnalyze` files (source-first) for later LLM use
- Emits `docs/wiki/inventory.md` with a machine-readable summary
- `agent-wiki status --analyze` runs a live inventory without writing the wiki

## Development

```bash
dotnet build AgentWiki.slnx
dotnet test AgentWiki.slnx
```

## License

MIT (or internal — TBD)
