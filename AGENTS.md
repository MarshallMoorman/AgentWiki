# AGENTS.md — AgentWiki (this repository)

Instructions for coding agents working **on AgentWiki itself** (not on a target consumer repo).

## Start here

1. Read **`docs/HANDOFF.md`** (session continuity, architecture, recent fixes, versioning).
2. Read **`README.md`** for user-facing commands and configuration.
3. Read **`CONTRIBUTING.md`** before adding features or providers.
4. Skim **`AgentWiki-Project-Specification.md`** only if changing product scope.

## Product snapshot

- **CLI:** `agent-wiki` (Spectre.Console.Cli)
- **Runtime:** .NET 10 / C# latest
- **Projects:** `AgentWiki.Cli` (app), `AgentWiki.Core` (models/helpers), `AgentWiki.Cli.Tests`
- **AI:** Microsoft.SemanticKernel → OpenAI / Azure OpenAI / GitHub Models
- **Version:** keep `Directory.Build.props` and `AgentWikiConstants.Version` in sync (`/bump-version` skill)

## Generated wiki for *this* repo

<!-- BEGIN AGENTWIKI -->
## AgentWiki Documentation
This repository maintains an **agent-optimized wiki** at `docs/wiki/`.

**For any task involving this codebase:**
1. Start by reading `docs/wiki/index.md` and `docs/wiki/architecture.md`
2. Drill into specific modules under `docs/wiki/modules/`
3. Review cross-cutting concerns under `docs/wiki/cross-cutting/` when relevant
4. The wiki is kept up-to-date via `agent-wiki generate` / `update` (and CI when configured). Do not ignore it.
5. Prefer wiki paths as a starting map, but always verify against source before making changes.
<!-- END AGENTWIKI -->

> Note: `docs/wiki/` may lag code after large changes. Prefer source + `docs/HANDOFF.md` over a stale offline wiki sample.

## Build, test, pack

```bash
dotnet build AgentWiki.slnx
dotnet test AgentWiki.slnx
./scripts/pack-and-install-tool.sh
agent-wiki --version
```

## Logs while debugging CLI behavior

- Directory: `~/.agentwiki/logs/`
- Today: `~/.agentwiki/logs/agent-wiki-YYYYMMDD.log`
- Terminal is intentionally quiet (Spectre only); details go to the log file.

## Coding conventions

- Clean architecture: abstractions in Core, IO/SK/git in Cli.
- Primary constructors, file-scoped namespaces, nullable enabled.
- Never log API keys or full prompt/response bodies by default.
- LLM parse paths must tolerate free-form JSON (see `LlmJson`, architecture_overview blobs).
- Offline fallback must keep working when LLM fails.
- After user-facing behavior changes, update README and HANDOFF.

## Do not

- Re-scaffold the solution from the spec unless explicitly asked.
- Bump version in only one of props/constants.
- Force-push or change git config.
- Commit secrets, `.env`, or `last-run.json` with keys.

## Common continuation tasks

| Ask | Where to work |
|-----|----------------|
| Bad wiki / LLM output | `Prompts/`, `ArchitectureGenerator`, `WikiGenerationOrchestrator`, `LlmJson` |
| CLI UX / logging | `Infrastructure/AgentWikiLogging.cs`, Commands |
| Analysis / gitignore | `RepoAnalyzer`, `GitIgnoreMatcher`, `FileCategorizer` |
| Incremental update | `GitChangeDetector`, `LastRunStore`, `SemanticWikiGenerator` |
| Config / init | `ConfigLoader`, `InitService`, `DotEnvLoader` |
| Release | `/bump-version`, `scripts/pack-and-install-tool.sh` |
