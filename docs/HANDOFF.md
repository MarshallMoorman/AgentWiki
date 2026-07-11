# AgentWiki — session handoff (for new conversations)

**Last updated:** 2026-07-10  
**Current version:** **1.1.0** (working toward **1.2.x** per single-repo polish plan)  
**Repo:** this repository root  
**Active plan:** [`docs/plans/docs-plan-single-repo-polish-v1.2.md`](plans/docs-plan-single-repo-polish-v1.2.md)

| Surface | Package | Command |
|---------|---------|---------|
| CLI (CI/agents primary) | `AgentWiki.Cli` | `agent-wiki` |
| Desktop companion | `AgentWiki.Desktop` | `agent-wiki-ui` |

This document is the single best place for a new coding agent or human to continue work without re-deriving session history.

**Session hygiene:** commit after each completed turn (product fix + tests + docs) so history stays reviewable; do not batch many unrelated changes into one commit. **v1.2 plan:** hard commit point after each phase.

**Git (as of this handoff):** Phases 1–4 committed; Phase 5 (cost/observability/dry-run) ready to commit. Do **not** publish to NuGet.org (local pack / Azure Artifacts later).

---

## 1. What this project is

**AgentWiki** is a native **.NET 10** product that:

1. Analyzes a repository (gitignore-aware inventory)
2. Optionally calls an LLM (Semantic Kernel + OpenAI / Azure OpenAI / GitHub Models)
3. Writes an **agent-optimized wiki** under `docs/wiki/`
4. Bootstraps **`AGENTS.md`** with instructions to read that wiki
5. Supports **incremental updates** via git change detection (`.agentwiki/last-run.json`)

It is intentionally file-based Markdown (not a RAG vector DB). Spec source of truth: `AgentWiki-Project-Specification.md`.

**Two hosts, one engine:**

- **CLI** — automation, CI, scripts  
- **Desktop** — Avalonia 12 interactive UI with full command parity  

Both call **`AgentWiki.App`** services (never put Spectre or Avalonia in App).

---

## 2. How to run (day-to-day)

```bash
# From repo root
dotnet build AgentWiki.slnx
dotnet test AgentWiki.slnx          # ~99 CLI tests + ~9 Desktop ViewModel tests

# Pack + install both global tools (local artifacts only)
./scripts/pack-and-install-tool.sh              # CLI + Desktop
./scripts/pack-and-install-tool.sh --cli-only
./scripts/pack-and-install-tool.sh --desktop-only

agent-wiki --version                # 1.1.0
agent-wiki-ui                       # launches Desktop GUI

# From source without tool install
dotnet run --project src/AgentWiki.Cli -- generate --repo-path . --force
./scripts/run-desktop.sh

# Target-repo workflow
agent-wiki init --repo-path /path/to/repo
agent-wiki test-provider --repo-path /path/to/repo
agent-wiki generate --repo-path /path/to/repo --force
agent-wiki update --repo-path /path/to/repo
agent-wiki status --repo-path /path/to/repo --analyze
```

**Logs (always):** `~/.agentwiki/logs/agent-wiki-YYYYMMDD.log`  
CLI: Spectre owns the terminal; file logging always on; `--verbose` streams diagnostics.  
Desktop: same log directory; no Spectre console sink by default.

**Desktop nupkg size:** ~195 MB (Avalonia natives) — intentional reason for a **separate** tool package from the lean CLI.

---

## 3. Architecture (code map)

```
AgentWiki.slnx
├── src/AgentWiki.Core      # models, analysis, generation helpers, abstractions
├── src/AgentWiki.App       # services (SK, git, orchestrator, config) + AddAgentWikiServices()
├── src/AgentWiki.Cli       # thin Spectre host → PackAsTool agent-wiki
├── src/AgentWiki.Desktop   # Avalonia 12 MVVM → PackAsTool agent-wiki-ui
└── tests/
    ├── AgentWiki.Cli.Tests
    └── AgentWiki.Desktop.Tests
```

| Layer | Path | Role |
|-------|------|------|
| Core models | `src/AgentWiki.Core/Models/` | Config, wiki docs, generation results |
| Core analysis | `src/AgentWiki.Core/Analysis/` | Gitignore, categorization, summary, **RoslynStaticAnalyzer**, `LlmSettings` |
| Core generation | `src/AgentWiki.Core/Generation/` | Markdown renderers, offline planners, flexible LLM JSON, **WikiPostProcessor** |
| **App services** | `src/AgentWiki.App/Services/` | Analyzer, SK LLM, orchestrator, git, bootstrap |
| App DI | `src/AgentWiki.App/ServiceCollectionExtensions.cs` | `AddAgentWikiServices()` |
| Prompts | `src/AgentWiki.App/Prompts/` | Embedded defaults; override via `.agentwiki/prompts/` |
| Logging | `src/AgentWiki.App/Infrastructure/AgentWikiLogging.cs` | Shared file logs |
| CLI | `src/AgentWiki.Cli/` | Commands + `CliConsole` (Spectre UX only) |
| Desktop | `src/AgentWiki.Desktop/` | Views, ViewModels, theme, markdown preview fixes |
| Pack | `scripts/pack-and-install-tool.sh` | Both tools; `--cli-only` / `--desktop-only` |
| Plan | `docs/plans/ui-companion-avalonia.md` | UI design plan (v1 **implemented**) |

### Generation pipeline

```
RepoAnalyzer
  → (optional) RoslynStaticAnalyzer → analysis.StaticAnalysis
  → (update only) GitChangeDetector + IncrementalScope
  → WikiGenerationOrchestrator
       1. ArchitectureGenerator (LLM or offline; offline uses static symbols)
       2. Module plan (LLM full runs / offline)
       3. Module pages (per module, LLM or offline + Roslyn types/endpoints)
       4. Cross-cutting pages
       5. Index + support pages
       6. WikiPostProcessor guardrails
  → MarkdownOutputWriter
  → AgentBootstrapper (AGENTS.md)
  → LastRunStore (.agentwiki/last-run.json)
```

Progress: `WikiGenerationRequest.Progress` (`IProgress<string>`). Cancellation token threaded through generator/orchestrator/LLM.

Post-process: after structured docs + after section render, `IWikiPostProcessor` cleans paths/deps/deprecation/links (configurable).  
Static analysis: syntax-only Roslyn (no compile); graceful skip for non-.NET / failures.

### Config priority (highest wins)

1. CLI / UI overrides  
2. Repo-root `.env`  
3. `.agentwiki/config.json`  
4. Process env `AGENTWIKI_*` (nested `__`)  
5. Tool `appsettings.json`  

**Secrets** → `.env` / CI. **Non-secrets** → `config.json`.  
Desktop Settings: non-secrets → config.json; API keys → `.env` only.

Key knobs: `provider`, `defaultModel`, `openAI.*`, `azureOpenAI.*`, `llmTimeoutSeconds` (default 300), `maxLlmSummaryChars` (16000), `enablePostProcessing` (default true), `postProcessingMode` (`lenient` \| `strict`), `enableRoslynAnalysis` (default true), `maxProjectsToAnalyze` (20), `maxSourceFilesForRoslyn` (200), `enableApiEndpointDocs` (default true), `enableEndpointLlmEnrichment`, `endpointIncludePatterns` / `endpointExcludePatterns`, `maxModules` (16), `maxFilesPerModule` (40), `moduleRoots` / `moduleGlobs`, `includeTestProjectsAsModules`, `applicationInsightsConnectionString`, cost rate overrides, `maxFilesToAnalyze`, `ignorePatterns`.

**Paths:** `~` expansion; wiki Markdown uses **repo-relative** paths only. Post-processor rewrites accidental absolute paths after generation.

---

## 4. Important product decisions (do not re-litigate casually)

| Decision | Rationale |
|----------|-----------|
| CLI primary for CI | Desktop is human companion only |
| Two tool packages | Keep CLI nupkg small; Desktop ~195 MB Avalonia |
| Spectre owns terminal | Serilog file-only by default |
| Offline fallback always | Wiki without LLM credentials |
| Flexible LLM JSON | Models ignore strict schemas |
| No temperature by default | Many modern models reject sampling params |
| JSON prompts include word `json` | OpenAI `json_object` format requirement |
| Incremental updates via git | CI-friendly |
| **No NuGet.org publish** | Local pack + GH artifacts; Azure Artifacts later |

---

## 5. What landed recently

### v1.2 Phase 5 — Cost, Observability & Dry-Run (ready to commit)

- **Dry-run plan:** create / update / unchanged classification (content-aware)  
- **Run summary:** correlation ID, steps, modules, offline flag, tokens, est. cost (CLI + Desktop)  
- **CostEstimator:** more models + config overrides (`inputUsdPerMillionTokens`, `modelPricing`)  
- **Status:** post-processing / Roslyn / API docs / App Insights knobs; meta cost fields  
- **Optional App Insights:** REST custom events when connection string set (off by default)  
- Meta JSON includes `estimatedUsd`  
- Tests: dry-run writer + cost overrides; suite **128 CLI + 9 Desktop**

| Hotspot | Path |
|---------|------|
| Writer | `MarkdownOutputWriter` → `OutputWriteResult` |
| Result | `GenerationResult` (dry-run + correlation + steps) |
| Telemetry | `ApplicationInsightsRunTelemetry` / `IRunTelemetry` |
| CLI | `GenerateCommand.RenderResult`, `StatusCommand` |
| Desktop | `GenerationViewModelBase.ApplyResult` |

### v1.2 Phase 4 — Module Discovery Improvements (committed `ee010ef`)

- Configurable **`maxModules`** (default **16**, was hard-coded 8) and **`maxFilesPerModule`** (40)
- Config **`moduleRoots`** / **`moduleGlobs`** + **`includeTestProjectsAsModules`**
- Offline planner prefers: config roots → globs → **.sln project entries** → .csproj → folders
- Project kind detection (web / function / worker / console / test / library) from Sdk + path
- Stable path-based ids, better related-file association; tests deprioritized by default
- LLM `ModulePlanPrompt` uses `{{MaxModules}}` and solution/project guidance
- Tests: `OfflineModulePlannerTests`; suite **125 CLI + 9 Desktop**

| Hotspot | Path |
|---------|------|
| Planner | `OfflineModulePlanner.cs` |
| Config | `AgentWikiConfig.MaxModules`, `ModuleRoots`, `ModuleGlobs` |
| Prompt | `ModulePlanPrompt.txt` |
| Orchestrator | `ResolveMaxModules` + `Plan(analysis, config)` |

### v1.2 Phase 3 — API Endpoint Documentation (committed `e49dd0b`)

- Top-level **`api-endpoints.md`** catalog (method, route, handler, kind, auth, params, purpose)  
- Per-module **Endpoints / Public API** section + key-components public API table  
- Index / getting-started navigation updated  
- `EndpointCatalog` filters via include/exclude patterns; default heuristic descriptions  
- Optional LLM description enrichment when credentials available (`enableEndpointLlmEnrichment`)  
- Config: `enableApiEndpointDocs`, `enableEndpointLlmEnrichment`, `endpointIncludePatterns`, `endpointExcludePatterns`  
- Tests: `ApiEndpointsMarkdownRendererTests` + orchestrator; suite **120 CLI + 9 Desktop**

| Hotspot | Path |
|---------|------|
| Renderer | `ApiEndpointsMarkdownRenderer.cs` |
| Filter/attach | `EndpointCatalog.cs` |
| Orchestrator | step `api-endpoints:{n}` + optional `endpoint-llm-enrichment` |
| Module model | `ModuleDocument.Endpoints` |

### v1.2 Phase 2 — Richer Offline + Roslyn (committed `ab135d8`)

- **`IStaticAnalyzer` / `RoslynStaticAnalyzer`** — syntax-only C# walk  
- Offline architecture + modules enriched with symbols  
- Package: `Microsoft.CodeAnalysis.CSharp` 4.14.0 on Core  

### v1.2 Phase 1 — Foundation & Guardrails (committed `e2f79ac`)

- **`IWikiPostProcessor` / `WikiPostProcessor`** — paths, deps, deprecation, link hygiene  
- Plan: `docs/plans/docs-plan-single-repo-polish-v1.2.md`

### v1.1.0 — App extraction + Desktop

- Extracted **`AgentWiki.App`**; CLI thin Spectre host; Desktop Avalonia 12 → `agent-wiki-ui`  
- Commits: `29d2842`, `05aee50`, HANDOFF refresh `8655b74`

### Historical CLI fixes (1.0.x) still relevant

| Version | Fix |
|---------|-----|
| 1.0.8–1.0.10 | Config merge layers, defaultModel precedence, timeout env not clobbered by missing JSON keys |
| 1.0.5–1.0.6 | Flexible LLM JSON / architecture_overview markdown blob |

### Known remaining polish (after Phase 5)

- Cost estimates are approximate (not billing-grade)  
- App Insights uses lightweight REST ingestion (not full SDK)  
- Desktop theme switching still Phase 6  
- **Next plan phase:** Phase 6 — Azure DevOps sample + Desktop theme + docs polish

---

## 6. Versioning

Keep in sync:

- `Directory.Build.props` (`Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`)
- `src/AgentWiki.Core/Constants/AgentWikiConstants.cs` (`Version` const)

```bash
./.grok/skills/bump-version/scripts/bump-version.sh patch   # or minor|major|X.Y.Z
./scripts/pack-and-install-tool.sh
agent-wiki --version
```

Slash skill: `/bump-version`.

---

## 7. Testing expectations

```bash
dotnet test AgentWiki.slnx
```

- Offline unit tests only in CI (no live LLM)  
- Mock `ILlmCompletionService` for orchestrator/parse tests  
- Integration: `tests/AgentWiki.Cli.Tests/Integration/EndToEndOfflineTests.cs`  
- Desktop: ViewModel/config-editor tests (no full UI E2E required)

---

## 8. Target-repo layout (what `init` creates)

```
.agentwiki/
  config.json
  prompts/          # optional overrides
  .gitignore        # ignores last-run.json
  last-run.json     # after first successful run (local)
.env.example
docs/wiki/
AGENTS.md           # bootstrap block
```

Desktop-only: `~/.agentwiki/ui-settings.json` (recent repos).

---

## 9. Suggested next work

**Follow `docs/plans/docs-plan-single-repo-polish-v1.2.md` phases (commit after each):**

1. ~~Phase 1 — WikiPostProcessor / guardrails~~ → **committed**  
2. ~~Phase 2 — Richer offline + Roslyn~~ → **committed**  
3. ~~Phase 3 — API endpoint documentation~~ → **committed**  
4. ~~Phase 4 — Module discovery improvements~~ → **committed**  
5. ~~Phase 5 — Cost, observability, dry-run~~ → **done (awaiting commit)**  
6. **Phase 6 — Azure DevOps sample + Desktop theme + docs polish**  

Out of scope for this plan: multi-repo workspace, vector search, publishing.

### CI (this repo)

| Workflow | Role |
|----------|------|
| `.github/workflows/ci.yml` | Build, test, pack CLI + Desktop, smoke-install tools |
| `.github/workflows/wiki-refresh.yml` | Offline dogfood wiki PR |
| `examples/github-actions/agent-wiki-update.yml` | **Consumer template** |

---

## 10. Files a new agent should read first

1. **This file** — `docs/HANDOFF.md`  
2. `README.md` — user-facing commands and tools  
3. `AGENTS.md` — coding rules for this repo  
4. `docs/plans/ui-companion-avalonia.md` — UI plan (implemented)  
5. `src/AgentWiki.App/ServiceCollectionExtensions.cs` + `Services/WikiGenerationOrchestrator.cs`  
6. `src/AgentWiki.Cli/Program.cs` / `src/AgentWiki.Desktop/` hosts  
7. Logs: `~/.agentwiki/logs/` when debugging runs  
8. Spec only if changing product scope: `AgentWiki-Project-Specification.md`  

### Desktop hotspots (if continuing UI)

| Concern | Where |
|---------|--------|
| Shell / nav | `Views/MainWindow.axaml`, `ViewModels/MainViewModel.cs` |
| Theme | `Themes/AppTheme.axaml`, `Styles/AppStyles.axaml` |
| Markdown fonts | `Services/MarkdownFontFix.cs`, `Services/AppFonts.cs` |
| Link clicks | `Services/WikiHyperlinkCommand.cs`, `Views/WikiBrowserView.axaml.cs` |
| Tool pack | `AgentWiki.Desktop.csproj` (`PackAsTool`, `ToolCommandName=agent-wiki-ui`) |

---

## 11. One-liner for a new conversation

> Continue AgentWiki **v1.1.0** (.NET 10): shared `AgentWiki.App` engine; global tools **`agent-wiki`** (CLI/CI) and **`agent-wiki-ui`** (Avalonia 12 Desktop companion). Generates agent-optimized Markdown wikis via RepoAnalyzer + Semantic Kernel multi-step pipeline, offline fallback, git incremental updates, logs at `~/.agentwiki/logs`. Do not publish to NuGet.org. Read **`docs/HANDOFF.md`** first; do not re-scaffold.
