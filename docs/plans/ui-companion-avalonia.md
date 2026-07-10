# Plan: AgentWiki UI Companion (Avalonia 12)

**Status:** Implemented (v1 parity)  
**Date:** 2026-07-10  
**Product version at plan time:** 1.0.10  
**Implemented:** `AgentWiki.App` extraction + Avalonia 12.1 Desktop (CommunityToolkit.Mvvm); CLI remains primary for CI.  
**Tool packaging:** separate nupkg `AgentWiki.Desktop` → global command `agent-wiki-ui` (not merged into `AgentWiki.Cli`).  
**Goal:** Ship a cross-platform desktop companion that supports **full CLI feature parity**, while keeping `agent-wiki` as a first-class product.

Use this document to start a **new implementation conversation**. Do not re-scaffold the CLI; extend the existing solution.

---

## 1. Product intent

| Principle | Detail |
|-----------|--------|
| CLI stays primary for CI/agents | `agent-wiki` remains the automation surface (GitHub Actions, scripts, Azure Pipelines). |
| UI is a companion | Same engine, friendlier discovery for humans who prefer not to live in a terminal. |
| One config model | UI reads/writes the same `.agentwiki/config.json`, `.env`, and `AGENTWIKI_*` rules as the CLI. |
| No second pipeline | UI must call the **same services** as the CLI (orchestrator, analyzer, SK LLM, git change detection). |
| Offline first | Works without LLM credentials; shows clear “offline vs live” status. |

**Non-goals (v1 UI):**

- Mobile / browser-first hosting (Avalonia can target more later; start desktop: macOS, Windows, Linux).
- Full Markdown wiki editor / CMS (preview + open in external editor is enough).
- NuGet.org or Azure Artifacts publishing from the UI.
- Replacing Spectre logging model with a different product architecture.

---

## 2. CLI feature parity matrix

Every CLI command must have a UI equivalent.

| CLI command | Core options | UI surface |
|-------------|--------------|------------|
| `init` | `--repo-path`, `--force` | **Setup** wizard: pick repo folder, preview files to create, Force overwrite toggle, Run Init |
| `generate` | `--repo-path`, `--output`, `--model`, `--provider`, `--force`, `--dry-run`, `--verbose` | **Generate** tab: all options as form fields; progress; results table; warnings |
| `update` | same generation options (force implied in CLI today) | **Update** tab: dry-run toggle; show change detection summary before/after |
| `status` | `--repo-path`, `--analyze` | **Dashboard**: config resolution table, last-run, meta, optional live inventory |
| `test-provider` | `--model`, `--provider` | **Connection** panel: Test button, latency, truncated reply (never log secrets) |
| Logging | `~/.agentwiki/logs/` | **Logs** view: open log folder, tail today’s file, copy path |
| Config layers | CLI > `.env` > config.json > process env > appsettings | **Settings** editor: edit config.json + optional .env (masked secrets); show effective resolved values |
| Paths | `~` expansion, repo-relative wiki paths | Folder pickers + display of expanded path; never write absolute machine paths into wiki |

**Parity also means:**

- Same defaults (`llmTimeoutSeconds`, `maxLlmSummaryChars`, ignore patterns).
- Same progress steps (analyze → architecture → modules → cross-cutting → write).
- Same `GenerationResult` fields (tokens, cost estimate, files written, warnings).
- Same offline fallback messaging.

---

## 3. Target UX (screens)

### 3.1 Shell layout

```
┌─────────────────────────────────────────────────────────────┐
│ AgentWiki  [Repo: ~/dev/foo ▾]  [Open folder]  v1.x.x      │
├──────────┬──────────────────────────────────────────────────┤
│ Dashboard│  Content area (selected page)                    │
│ Generate │                                                  │
│ Update   │                                                  │
│ Setup    │                                                  │
│ Settings │                                                  │
│ Provider │                                                  │
│ Wiki     │                                                  │
│ Logs     │                                                  │
└──────────┴──────────────────────────────────────────────────┘
│ Status bar: provider · effective model · timeout · ready? · log path │
```

### 3.2 Dashboard (status + analyze)

- Effective config table (mirror improved CLI status: defaultModel vs effective model, timeout source tip, LLM ready reason).
- Last successful run card.
- Optional **Analyze inventory** with category counts and top folders.
- Quick actions: Generate, Update, Open wiki folder, Open AGENTS.md.

### 3.3 Generate / Update

- Shared form: Output path, Provider, Model override, Force, Dry-run, Verbose.
- Large **Run** button; cancel via `CancellationToken` (add cooperative cancel if not fully wired today).
- Live progress list (bind to `IProgress<string>` already on `WikiGenerationRequest`).
- Results: duration, tokens, cost estimate, files written, warnings, change-detection details (update).

### 3.4 Setup (init)

- Repo path picker.
- Diff preview: which of config / .env.example / prompts / .gitignore will be created.
- Force overwrite.
- Post-init checklist (edit config, set secrets, test provider, generate).

### 3.5 Settings

- Form bound to `AgentWikiConfig` + nested Azure/OpenAI options.
- Ignore patterns multi-line editor.
- Save to `.agentwiki/config.json` (non-secrets).
- Secrets: prefer editing/creating `.env` with masked fields; never write API keys into wiki or logs.
- **Effective values** read-only panel after load (shows layering outcome).
- Validate JSONC comments still OK if user pastes advanced config.

### 3.6 Provider test

- Provider + model fields.
- Test connection → success/fail, timing, short reply preview.
- Clear errors without stack traces (link “Open log file”).

### 3.7 Wiki browser (companion value-add)

- Tree of `docs/wiki/**`.
- Markdown preview (Avalonia Markdown control or WebView for rendered HTML).
- Open in system editor / reveal in Finder-Explorer.
- Not a full CMS — read-focused.

### 3.8 Logs

- List files under `~/.agentwiki/logs/`.
- Tail last N lines; auto-refresh optional.
- Open external.

---

## 4. Architecture

### 4.1 Problem with today

Almost all “app” services live under `AgentWiki.Cli` (Spectre + filesystem + SK + git). Core holds models, analysis, generation helpers, abstractions. A UI must **not** depend on Spectre commands.

### 4.2 Recommended solution structure

```
AgentWiki.slnx
├── src/AgentWiki.Core          # existing (models, analysis, generation, abstractions)
├── src/AgentWiki.App           # NEW: application services moved from Cli (no Spectre, no Avalonia)
├── src/AgentWiki.Cli           # thin Spectre host → AgentWiki.App
├── src/AgentWiki.Desktop       # NEW: Avalonia 12 UI host → AgentWiki.App
└── tests/
    ├── AgentWiki.Cli.Tests     # existing (retarget references as needed)
    ├── AgentWiki.App.Tests     # NEW: service tests if split warrants
    └── AgentWiki.Desktop.Tests # NEW: optional view-model tests (headless)
```

### 4.3 Move map (Cli → App)

Move these from `AgentWiki.Cli` into `AgentWiki.App` (same namespaces or `AgentWiki.App.*` — prefer **keeping namespaces stable** first to minimize churn, or rename in one PR carefully):

| Type | Notes |
|------|--------|
| `ConfigLoader`, `DotEnvLoader` | Config layers |
| `InitService` | Scaffold |
| `RepoAnalyzer` | Inventory |
| `GitChangeDetector`, `GitProcess` | Incremental |
| `SemanticKernelLlmCompletionService`, `LlmResilience` | LLM |
| `ArchitectureGenerator`, `WikiGenerationOrchestrator` | Pipeline |
| `SemanticWikiGenerator`, `PlaceholderWikiGenerator` | Facade |
| `MarkdownOutputWriter`, `AgentBootstrapper` | Output |
| `PromptManager` | Prompts (+ embed prompt files in App or shared) |
| `LastRunStore` | State |
| `AgentWikiLogging` | Adapt: file sink shared; console sink CLI-only |

**Keep in Cli:**

- `Program.cs`, Spectre commands, Spectre result rendering, `TypeRegistrar`.

**Keep pure in Core:**

- Models, `LlmJson`, renderers, offline planners, `PathUtility`, `EnvConfigApplier`, `LlmSettings`, `FileCategorizer`, etc.

### 4.4 DI composition

```
AgentWiki.App.ServiceCollectionExtensions.AddAgentWikiCore(services)
  → registers all I* services

AgentWiki.Cli  → AddAgentWikiCore + logging console policy + Spectre commands
AgentWiki.Desktop → AddAgentWikiCore + Avalonia DI + ViewModels
```

Single version constant remains `AgentWikiConstants.Version` (+ `Directory.Build.props`).

### 4.5 Progress & cancellation

Already partially present:

- `WikiGenerationRequest.Progress` (`IProgress<string>`)

Plan work:

1. Thread `CancellationToken` through long LLM calls (verify SK respects it; surface Cancel in UI).
2. Optional richer progress: `IProgress<WikiProgressEvent>` with step enum + percent estimate (backward compatible).
3. UI ViewModels subscribe on UI thread (`Dispatcher.UIThread`).

### 4.6 Logging

| Channel | CLI | Desktop |
|---------|-----|---------|
| File `~/.agentwiki/logs/` | yes | yes (same paths) |
| Terminal Spectre | yes | no |
| In-app log panel | no | yes (optional Serilog sink or file tail) |

Never log API keys or full prompt bodies by default (existing rule).

---

## 5. Avalonia 12 stack (2026)

Avalonia **12** is released and targets modern .NET (docs recommend **.NET 10** for Avalonia 12). Align with this repo’s `net10.0`.

Suggested packages (pin exact versions at implementation time):

- `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`
- `Avalonia.Fonts.Inter` (or system fonts)
- CommunityToolkit.Mvvm or Avalonia-friendly MVVM (ReactiveUI optional — prefer simpler CommunityToolkit.Mvvm for maintainability)
- Markdown: `Markdown.Avalonia` **or** Avalonia WebView for preview (evaluate Avalonia 12 compatibility)
- DI: `Microsoft.Extensions.DependencyInjection` (already used)

**Platforms v1:** macOS (dev machine), Windows, Linux desktop.  
**Later:** optional single-project multi-target if desired; not required for v1.

Project template sketch:

```bash
dotnet new avalonia.mvvm -n AgentWiki.Desktop -o src/AgentWiki.Desktop --framework net10.0
# then retarget packages to Avalonia 12 and wire App DI
```

---

## 6. Implementation phases

### Phase 0 — Extract `AgentWiki.App` (blocking foundation)

**Why first:** Without this, UI will duplicate CLI logic or drag Spectre into desktop.

Deliverables:

1. Create `AgentWiki.App` class library (`net10.0`).
2. Move services + prompts embedding from Cli → App.
3. Cli becomes thin host; all existing tests green.
4. Document DI entrypoint `AddAgentWikiServices()`.
5. No UI yet.

**Exit criteria:** `dotnet test` pass; `agent-wiki` behavior unchanged for init/generate/update/status/test-provider.

### Phase 1 — Desktop shell + Dashboard + Settings

Deliverables:

1. Avalonia 12 app project + Fluent theme.
2. Repo picker + recent repos list (store in `~/.agentwiki/ui-settings.json`).
3. Dashboard wired to `IConfigLoader`, `ILastRunStore`, optional `IRepoAnalyzer`.
4. Settings editor save config.json; masked .env editor.
5. Open log directory / show today’s log path.

**Exit criteria:** User can open a repo, see status equivalent, edit non-secret config, save.

### Phase 2 — Generate / Update / Progress / Results

Deliverables:

1. Generate & Update views binding to `IWikiGenerator`.
2. Progress UI from `IProgress<string>`.
3. Dry-run + Force.
4. Results + warnings display.
5. Cancellation.

**Exit criteria:** Full generate/update from UI matches CLI outputs for same repo/config (offline).

### Phase 3 — Init + Test Provider + Wiki browser

Deliverables:

1. Init wizard with force.
2. Test-provider panel.
3. Wiki file tree + Markdown preview + reveal in OS.
4. Status bar “LLM ready” with reason string (`LlmSettings.DescribeNotReadyReason`).

**Exit criteria:** Full CLI command parity from UI; dogfood on AgentWiki + LoanView.

### Phase 4 — Packaging & polish

Deliverables:

1. Local run scripts: `scripts/run-desktop.sh`.
2. Optional: `dotnet publish` profiles for macOS/Windows/Linux self-contained or framework-dependent.
3. README section “Desktop companion”.
4. CI: build Desktop project (compile + unit tests); optional headless smoke (no full UI E2E required initially).
5. Accessibility basics (labels, keyboard nav), dark/light Fluent.
6. HANDOFF + AGENTS.md update.

**Exit criteria:** Contributor can clone, build, run desktop on macOS; CI compiles Desktop.

### Phase 5 (optional later)

- Azure Artifacts install instructions for tool + separate desktop zip/dmg.
- In-app “Install/update CLI tool from local pack”.
- Multi-repo workspace / job queue.
- Diff view of wiki changes before write (dry-run enriched).

---

## 7. ViewModel design sketch

```
MainViewModel
  RepoPath, RecentRepos
  Navigation (enum Page)

DashboardViewModel
  LoadAsync() → Config, LastRun, Meta
  AnalyzeCommand → RepoAnalysisResult

GenerateViewModel / UpdateViewModel
  Options → WikiGenerationRequest
  RunCommand, CancelCommand
  ProgressLines, Result

SettingsViewModel
  Editable AgentWikiConfig clone
  SaveCommand, ReloadCommand
  EffectiveSnapshot (read-only)

InitViewModel
  Force, Preview, Run

ProviderViewModel
  TestCommand → LlmCompletionResult summary

WikiBrowserViewModel
  Tree, SelectedMarkdown, Refresh

LogsViewModel
  Files, Tail
```

All ViewModels depend only on **Core abstractions + App services**, never on Spectre.

---

## 8. Testing strategy

| Layer | What |
|-------|------|
| App services | Existing Cli.Tests mostly move/retarget; keep offline E2E. |
| ViewModels | Unit test with mocked `IWikiGenerator` / `IConfigLoader` (no Avalonia needed). |
| Desktop | Compile in CI; optional smoke with headless if available later. |
| Manual | Scripted checklist: init → status → test-provider → generate offline → update dry-run → open wiki. |

Do **not** call live LLM in CI.

---

## 9. Config & secrets UX rules

1. **Never** put secrets in `docs/wiki` or commit prompts that embed keys.
2. Settings Save writes:
   - Non-secrets → `.agentwiki/config.json`
   - Secrets → `.env` (create from `.env.example` if missing)
3. Show resolved priority banner (same as CLI status).
4. Folder pickers return absolute paths for the process; wiki generation continues to emit **repo-relative** content only.
5. Support `~` in typed path fields via existing `PathUtility`.

---

## 10. Packaging / distribution (near-term)

Aligned with current product decision: **no NuGet.org publish**; Azure Artifacts later.

| Artifact | Near-term |
|----------|-----------|
| CLI tool | Pack nupkg + CI artifact + `scripts/pack-and-install-tool.sh` |
| Desktop | `dotnet publish` zip/app per OS for internal use; not store submission |
| Versioning | Same `Directory.Build.props` version for Core/App/Cli/Desktop |

When Azure Artifacts arrives: publish CLI tool package first; desktop can remain side-car binaries.

---

## 11. Risks & mitigations

| Risk | Mitigation |
|------|------------|
| Large Cli → App move breaks tests | Phase 0 only; no UI until green |
| Avalonia Markdown package lag on v12 | Fallback: plain Text + external open; or WebView |
| LLM long runs freeze UI | Always async; progress; cancel token |
| Users expect UI to replace CI | Docs: CLI for automation, UI for interactive |
| Secret leakage in UI logs | Masked controls; reuse logging redaction rules |
| macOS notarization / signing | Out of scope v1; run from `dotnet run` / unsigned publish |

---

## 12. Suggested PR sequence (for execute-plan style work)

1. **PR1:** Create `AgentWiki.App`; move services; thin Cli; tests green.  
2. **PR2:** Avalonia Desktop shell + DI + Dashboard (read-only status).  
3. **PR3:** Settings + config/env save.  
4. **PR4:** Generate/Update + progress + results.  
5. **PR5:** Init + Test Provider.  
6. **PR6:** Wiki browser + Logs.  
7. **PR7:** Docs, scripts, CI compile Desktop, HANDOFF.

---

## 13. Success metrics

- Feature parity checklist (section 2) 100% for v1.  
- Offline generate from UI on AgentWiki repo produces same file set as CLI (allowing timestamps).  
- No Spectre dependency in Desktop.  
- No regression in CLI tests.  
- New user can init + generate without reading CLI help.

---

## 14. One-liner for a new implementation conversation

> Implement the AgentWiki Avalonia 12 desktop companion per `docs/plans/ui-companion-avalonia.md`: first extract `AgentWiki.App` from Cli services (feature-preserving), then build Desktop MVVM UI with full parity for init/generate/update/status/test-provider, shared config layers, progress via `WikiGenerationRequest.Progress`, offline-first, CLI remains primary for CI. Target net10 / Avalonia 12; do not publish to NuGet.org.

---

## 15. Open decisions (resolve at implementation start)

1. **MVVM library:** CommunityToolkit.Mvvm (recommended) vs ReactiveUI.  
2. **Markdown preview:** native Avalonia control vs WebView.  
3. **Recent repos:** count limit (e.g. 10) and clear history.  
4. **Whether Update should offer “preview changes only”** beyond dry-run (nice-to-have).  
5. **App display name:** `AgentWiki` vs `AgentWiki Desktop` vs `agent-wiki UI`.

Defaults if unspecified: CommunityToolkit.Mvvm, simplest Markdown preview that works on Avalonia 12, 10 recent repos, display name **AgentWiki Desktop**.
