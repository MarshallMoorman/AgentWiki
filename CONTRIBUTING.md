# Contributing to AgentWiki

Thanks for helping improve AgentWiki. This document describes how to extend the tool safely.

## Development setup

```bash
dotnet --version   # .NET 10 SDK
dotnet restore AgentWiki.slnx
dotnet build AgentWiki.slnx
dotnet test AgentWiki.slnx
```

Run the CLI from source:

```bash
dotnet run --project src/AgentWiki.Cli -- --help
dotnet run --project src/AgentWiki.Cli -- generate --repo-path . --force
```

## Architecture overview

| Project | Responsibility |
|---------|----------------|
| `AgentWiki.Core` | Models, abstractions, analysis/generation helpers (incl. Roslyn syntax analysis) |
| `AgentWiki.App` | Shared services (SK/LLM, orchestrator, git, config) via `AddAgentWikiServices()` |
| `AgentWiki.Cli` | Thin Spectre host / global tool `agent-wiki` |
| `AgentWiki.Desktop` | Avalonia companion / global tool `agent-wiki-ui` |
| `AgentWiki.Cli.Tests` | Unit + offline integration tests |
| `AgentWiki.Desktop.Tests` | ViewModel / settings unit tests |

Generation pipeline:

1. `RepoAnalyzer` — inventory + gitignore
2. `IStaticAnalyzer` (optional Roslyn) — symbols / endpoints
3. `IChangeDetector` — incremental scope (update mode)
4. `WikiGenerationOrchestrator` — multi-step content + post-processor + API endpoints
5. `IOutputWriter` — Markdown files (dry-run create/update/unchanged plan)
6. `IAgentBootstrapper` — `AGENTS.md`
7. `ILastRunStore` — `.agentwiki/last-run.json`

## Adding a new wiki section

1. Extend structured models in `AgentWiki.Core/Models` if needed.
2. Add a prompt under `src/AgentWiki.App/Prompts/` (embedded resource).
3. Wire a step in `WikiGenerationOrchestrator`.
4. Render Markdown via a dedicated renderer or existing helpers.
5. Ensure `index.md` navigation includes the new page.
6. Add unit tests for parse/render paths (prefer offline fixtures).

## Adding a new LLM provider

1. Extend `SemanticKernelLlmCompletionService.BuildKernel`.
2. Update `CanUseLiveLlm` / `NormalizeProvider`.
3. Document env vars in README and `.env.example` from `init`.
4. Keep secrets out of logs (never log API keys or full prompts by default).

## Customizing prompts

- **Repo overrides:** `.agentwiki/prompts/*.txt` (created by `init`)
- **Tool defaults:** embedded `src/AgentWiki.Cli/Prompts/*.txt`

Supported placeholders depend on the prompt (e.g. `{{RepoName}}`, `{{RepoSummary}}`, `{{ModuleName}}`).

## Testing guidelines

- Prefer deterministic offline paths (no live LLM in CI).
- Mock `ILlmCompletionService` for structured-JSON parsing tests.
- Use temp directories for filesystem tests; clean up in `finally`.
- Keep tests fast — avoid network calls.

## Pull requests

- Keep changes focused and documented.
- Run `dotnet build` and `dotnet test` before opening a PR.
- Update README when user-facing behavior changes.
- Do not commit secrets, `.env`, or API keys.

## CI

- **PR / push:** `.github/workflows/ci.yml` builds, tests, packs CLI + Desktop, uploads nupkgs as **workflow artifacts** (no external feed publish yet).
- **This repo wiki:** `.github/workflows/wiki-refresh.yml` regenerates `docs/wiki/` offline and opens a PR.
- **Consumer templates:**
  - GitHub: `examples/github-actions/agent-wiki-update.yml`
  - Azure DevOps: `examples/azure-pipelines/agent-wiki-update.yml`
- **Azure Artifacts / NuGet.org:** not configured yet; pack + artifact upload is enough until a feed is ready.
- **Do not publish to NuGet.org** from this repo without an explicit product decision.

## Desktop notes

- Theme preference (`system` / `dark` / `light`) lives in `~/.agentwiki/ui-settings.json` via `ThemeService` + `UiSettingsStore`.
- Keep Avalonia hosts thin; product logic stays in `AgentWiki.App` / `AgentWiki.Core`.

## Release checklist (maintainers)

1. Bump version: `./.grok/skills/bump-version/scripts/bump-version.sh patch` (or minor/major).
2. Ensure CI is green on `main`.
3. Smoke-test: `./scripts/pack-and-install-tool.sh && agent-wiki --version`.
4. Download the `agentwiki-nupkg` artifact from the CI run if you need to share the package.
5. (Later) Push the nupkg to Azure Artifacts when that pipeline is ready.
