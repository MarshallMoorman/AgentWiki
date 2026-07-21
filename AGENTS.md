# AGENTS.md — AgentWiki (this repository)

Instructions for coding agents working **on AgentWiki itself** (not on a target consumer repo).

## Start here

1. Read **`docs/HANDOFF.md`** (session continuity, architecture, recent fixes, versioning).
2. Read **`README.md`** for user-facing commands and configuration.
3. Read **`CONTRIBUTING.md`** before adding features or providers.
4. Skim **`AgentWiki-Project-Specification.md`** only if changing product scope.

## Product snapshot

- **CLI:** `agent-wiki` (Spectre.Console.Cli) — primary for CI/agents
- **Desktop companion:** Avalonia 12 UI — package `AgentWiki.Desktop`, command `agent-wiki-ui` (separate from CLI tool)
- **Runtime:** .NET 10 / C# latest
- **Projects:** `AgentWiki.Core` (models/helpers), `AgentWiki.App` (services/SK/git), `AgentWiki.Cli` (thin Spectre host / `agent-wiki`), `AgentWiki.Desktop` (Avalonia / `agent-wiki-ui`), tests
- **AI:** Microsoft.SemanticKernel → OpenAI / Azure OpenAI / GitHub Models
- **Version:** keep `Directory.Build.props` and `Constants.Product.Version` in sync (`/bump-version` skill)
- **Shared constants:** use `Constants.[Group].[Name]` from `src/AgentWiki.Core/Constants/Constants.cs` (no magic config defaults in call sites)
- **Agent docs:** full AGENTS.md via `IAgentsMdGenerator` / `agent-wiki agents`; generate only fills full AGENTS when missing/trivial; rich files keep `AgentBootstrapper` block updates. Generated AGENTS must include the self-update section (`Constants.AgentsMd.SelfUpdateSectionHeading`).
- **Workspace (multi-repo, file-based Phase 1):** `agent-wiki workspace init|generate|update|status|add` — system KB under `docs/knowledge-base/`; no vectors/embeddings in this phase. Reuse single-repo generators for members; keep single-repo commands unaffected.
- **Do not publish to NuGet.org** (local pack / Azure Artifacts later)

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

# Desktop companion tool (or from source)
./scripts/pack-and-install-tool.sh --desktop-only
agent-wiki-ui
# from source: ./scripts/run-desktop.sh
```

## Logs while debugging CLI behavior

- Directory: `~/.agentwiki/logs/`
- Today: `~/.agentwiki/logs/agent-wiki-YYYYMMDD.log`
- Terminal is intentionally quiet (Spectre only); details go to the log file.

## Coding conventions

- Clean architecture: abstractions in Core; services in App; Cli/Desktop are thin hosts.
- Register services via `AgentWiki.App.ServiceCollectionExtensions.AddAgentWikiServices()`.
- Primary constructors, file-scoped namespaces, nullable enabled.
- Never log API keys or full prompt/response bodies by default.
- LLM parse paths must tolerate free-form JSON (see `LlmJson`, architecture_overview blobs).
- Offline fallback must keep working when LLM fails.
- After user-facing behavior changes, update README and HANDOFF.

## Do not

- Re-scaffold the solution from the spec unless explicitly asked.
- Bump version in only one of props/`Constants.Product.Version`.
- Introduce new config/default magic numbers outside `Constants.cs`.
- Force-push or change git config.
- Commit secrets, `.env`, or `last-run.json` with keys.

## Common continuation tasks

| Ask | Where to work |
|-----|----------------|
| Full AGENTS.md / README auto-gen | `AgentsMdGenerator`, `ReadmeGenerator`, `AgentsMdOfflineBuilder`, `SemanticWikiGenerator.ApplyAgentsAndReadmeAsync`, `AgentsCommand` |
| Bad wiki / LLM output | `src/AgentWiki.App/Prompts/`, `ArchitectureGenerator`, `WikiGenerationOrchestrator`, `LlmJson`, `WikiPostProcessor` |
| Offline / Roslyn quality | `RoslynStaticAnalyzer`, `OfflineArchitectureGenerator`, `OfflineModulePlanner` |
| Module discovery | `OfflineModulePlanner`, `moduleRoots` / `moduleGlobs`, `MaxModules` |
| API endpoints | `EndpointCatalog`, `ApiEndpointsMarkdownRenderer`, orchestrator endpoint step |
| CLI UX / logging | `AgentWiki.App/Infrastructure/AgentWikiLogging.cs`, `AgentWiki.Cli/Commands`, `CliConsole` |
| Cost / dry-run / telemetry | `CostEstimator`, `MarkdownOutputWriter`, `IRunTelemetry`, `GenerateCommand.RenderResult` |
| Desktop UI | `src/AgentWiki.Desktop/` ViewModels + Views; theme: `ThemeService` + `ui-settings.json` |
| CI samples | `examples/github-actions/`, `examples/azure-pipelines/` |
| Analysis / gitignore | `RepoAnalyzer`, `GitIgnoreMatcher`, `FileCategorizer`, `IStaticAnalyzer` |
| Incremental update | `GitChangeDetector`, `LastRunStore`, `SemanticWikiGenerator` |
| Multi-repo workspace | `WorkspaceConfig` models, `WorkspaceConfigLoader`, `WorkspaceMemberResolver`, `CrossRepoSignalCollector`, `WorkspaceOfflineBuilder`, `WorkspaceOrchestrator`, `WorkspaceLastRunStore`, `WorkspaceCommands` |
| Config / init | `ConfigLoader`, `InitService`, `DotEnvLoader`; workspace: `WorkspaceInitService` |
| Release | `/bump-version`, `scripts/pack-and-install-tool.sh` |
| Step requirements | `docs/development/01-*.md`, `docs/development/02-multi-repo-workspace-phase1-*.md` |
