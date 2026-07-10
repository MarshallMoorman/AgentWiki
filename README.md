# AgentWiki

**AgentWiki** (`agent-wiki`) is a native **.NET 10** CLI that generates and maintains **agent-optimized documentation wikis** for codebases.

It analyzes a repository, optionally calls an LLM through **Microsoft.SemanticKernel** (OpenAI / Azure OpenAI / GitHub Models), and writes structured Markdown under `docs/wiki/` plus an `AGENTS.md` bootstrap block so coding agents start with durable context.

> **Version:** see `Directory.Build.props` / `agent-wiki --version` (currently 1.0.x).  
> **Handoff for new agents:** **[`docs/HANDOFF.md`](docs/HANDOFF.md)** — read this first in a new conversation.

## Why AgentWiki?

| Problem | AgentWiki approach |
|---------|-------------------|
| Stale internal wikis | `generate` / `update` from live inventory + optional LLM |
| Agents lack repo context | `AGENTS.md` points agents at `docs/wiki/` first |
| JS/Python-only pipelines | Fully native .NET + Semantic Kernel + Azure OpenAI |
| Expensive full rebuilds | Git-based incremental updates with section mapping |

**When to use AgentWiki vs RAG:** AgentWiki produces a **file-based, reviewable wiki** checked into the repo. Use RAG when you need semantic retrieval over large corpora without committing generated docs.

## Quick start

```bash
# Prerequisites: .NET 10 SDK
dotnet build AgentWiki.slnx
dotnet test AgentWiki.slnx

# Install/update the global tool
./scripts/pack-and-install-tool.sh
agent-wiki --version

# Scaffold config in a target repository
agent-wiki init --repo-path /path/to/repo

# Verify LLM credentials (optional)
agent-wiki test-provider --repo-path /path/to/repo

# Full generation (works offline without LLM credentials)
agent-wiki generate --repo-path /path/to/repo --force

# Incremental update (CI-friendly)
agent-wiki update --repo-path /path/to/repo

# Status + live inventory
agent-wiki status --repo-path /path/to/repo --analyze
```

From source without installing the tool:

```bash
dotnet run --project src/AgentWiki.Cli -- generate --repo-path /path/to/repo --force
```

## Architecture

```mermaid
flowchart TB
    CLI[agent-wiki CLI<br/>Spectre.Console] --> Config[ConfigLoader + .env]
    CLI --> Analyzer[RepoAnalyzer]
    CLI --> Changes[GitChangeDetector]
    CLI --> Orch[WikiGenerationOrchestrator]
    Orch --> Arch[ArchitectureGenerator / SK]
    Orch --> Modules[Module pages]
    Orch --> Cross[Cross-cutting pages]
    Orch --> Index[index + support pages]
    CLI --> Writer[MarkdownOutputWriter]
    CLI --> Boot[AgentBootstrapper]
    CLI --> LastRun[LastRunStore]
    Arch --> LLM[Azure OpenAI / OpenAI / GitHub Models]
    CLI --> Logs["~/.agentwiki/logs"]
```

| Project | Role |
|---------|------|
| `src/AgentWiki.Cli` | CLI commands, Semantic Kernel, git, filesystem, DI |
| `src/AgentWiki.Core` | Models, analysis, offline planners, flexible LLM JSON |
| `tests/AgentWiki.Cli.Tests` | xUnit + Shouldly + Moq |

## Commands

| Command | Description |
|---------|-------------|
| `agent-wiki init` | Create `.agentwiki/config.json`, sample prompts, `.env.example` |
| `agent-wiki generate` | Full multi-step wiki generation |
| `agent-wiki update` | Incremental update from git changes since last run |
| `agent-wiki status` | Config, last-run, log path, optional `--analyze` |
| `agent-wiki test-provider` | Verify LLM credentials with a minimal chat call |

### Common options

| Option | Description |
|--------|-------------|
| `-r, --repo-path` | Repository root (default: `.`) |
| `-o, --output` | Wiki output path (default: `docs/wiki`) |
| `-c, --config` | Path to config JSON |
| `-m, --model` | Model / Azure deployment name |
| `--provider` | `azure-openai` \| `openai` \| `github-models` |
| `--force` | Overwrite without confirmation (`generate`) |
| `--dry-run` | Analyze / report without writing files |
| `--verbose` | Stream diagnostics to console (file logging always on) |

## Configuration

**Priority (highest wins):** CLI → `.agentwiki/config.json` → `AGENTWIKI_*` env (and repo `.env`) → tool `appsettings.json`.

| Source | Best for | Required? |
|--------|----------|-----------|
| `config.json` | Provider, model, paths, timeouts, ignore patterns | Recommended |
| `.env` | API keys | Optional |
| CI env | Production secrets | Optional |

See [`examples/agentwiki.config.json`](examples/agentwiki.config.json).

Useful knobs:

- `llmTimeoutSeconds` (default **300**)
- `maxLlmSummaryChars` (default **16000**)
- `maxFilesToAnalyze`, `enableIncrementalUpdates`, `ignorePatterns`

## Logging

| What | Where |
|------|--------|
| Detailed diagnostics | `~/.agentwiki/logs/agent-wiki-YYYYMMDD.log` |
| Terminal UX | Spectre tables/spinners only (by default) |
| Shown on | `status`, start of generate/update, errors |

```bash
ls ~/.agentwiki/logs/
tail -f ~/.agentwiki/logs/agent-wiki-*.log
```

## Wiki output

Default: `docs/wiki/`

```
docs/wiki/
├── index.md
├── architecture.md
├── key-components.md
├── data-flows.md
├── inventory.md
├── glossary.md
├── getting-started.md
├── modules/*.md
├── cross-cutting/*.md
└── .agentwiki-meta.json
```

Generated docs describe the **current** codebase. Prompts instruct the model **not** to invent deprecation/legacy language unless the source has explicit markers (e.g. `[Obsolete]`).

## Incremental updates

`agent-wiki update` diffs against `.agentwiki/last-run.json`, maps changed files to modules/sections, skips work when nothing relevant changed, and rewrites only affected pages (+ support pages).

## Customizing prompts

| Source | Location |
|--------|----------|
| Tool defaults | `src/AgentWiki.Cli/Prompts/*.txt` (embedded) |
| Per-repo overrides | `.agentwiki/prompts/` (from `init`) |

## Versioning & release

```bash
./.grok/skills/bump-version/scripts/bump-version.sh patch   # or minor|major|X.Y.Z
./scripts/pack-and-install-tool.sh
agent-wiki --version
```

Also available as project skill: `/bump-version`.

## Development docs

| Doc | Purpose |
|-----|---------|
| [`docs/HANDOFF.md`](docs/HANDOFF.md) | **New conversation start** — full continuity |
| [`AGENTS.md`](AGENTS.md) | Agent rules for this repo |
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | How to extend |
| [`AgentWiki-Project-Specification.md`](AgentWiki-Project-Specification.md) | Original product spec |
| [`docs/wiki/`](docs/wiki/) | Sample/self wiki (may lag; regenerate after large changes) |

## CI

GitHub Actions: [`.github/workflows/agent-wiki-update.yml`](.github/workflows/agent-wiki-update.yml)  
Runs `update`, opens a PR when wiki files change. Works offline if LLM secrets are unset.

## License

[MIT](LICENSE)
