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
| `AgentWiki.Core` | Models, abstractions, pure analysis/generation helpers |
| `AgentWiki.Cli` | Spectre commands, DI wiring, SK/LLM, git, filesystem IO |
| `AgentWiki.Cli.Tests` | Unit + offline integration tests |

Generation pipeline:

1. `RepoAnalyzer` — inventory + gitignore
2. `IChangeDetector` — incremental scope (update mode)
3. `WikiGenerationOrchestrator` — multi-step content
4. `IOutputWriter` — Markdown files
5. `IAgentBootstrapper` — `AGENTS.md`
6. `ILastRunStore` — `.agentwiki/last-run.json`

## Adding a new wiki section

1. Extend structured models in `AgentWiki.Core/Models` if needed.
2. Add a prompt under `src/AgentWiki.Cli/Prompts/` (embedded resource).
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

- **PR / push:** `.github/workflows/ci.yml` builds, tests, packs, and uploads the nupkg artifact.
- **Tags `v*`:** same workflow optionally pushes to NuGet.org when `NUGET_API_KEY` is configured.
- **This repo wiki:** `.github/workflows/wiki-refresh.yml` regenerates `docs/wiki/` offline and opens a PR.
- **Consumer template:** `examples/github-actions/agent-wiki-update.yml` (copy into other repos).

## Release checklist (maintainers)

1. Bump version: `./.grok/skills/bump-version/scripts/bump-version.sh patch` (or minor/major).
2. Ensure CI is green on `main`.
3. Smoke-test: `./scripts/pack-and-install-tool.sh && agent-wiki --version`.
4. Tag and push: `git tag vX.Y.Z && git push origin vX.Y.Z` (triggers NuGet publish if secret is set).
5. Confirm the GitHub Actions artifact / NuGet package.
