# AgentWiki — session handoff (for new conversations)

**Last updated:** 2026-07-10  
**Current version:** 1.0.10  
**Repo:** this repository root (CLI command: `agent-wiki`)

This document is the single best place for a new coding agent or human to continue work without re-deriving session history.

**Session hygiene:** commit after each completed turn (product fix + tests + docs) so history stays reviewable; do not batch many unrelated changes into one commit.

---

## 1. What this project is

**AgentWiki** is a native **.NET 10** CLI tool that:

1. Analyzes a repository (gitignore-aware inventory)
2. Optionally calls an LLM (Semantic Kernel + OpenAI / Azure OpenAI / GitHub Models)
3. Writes an **agent-optimized wiki** under `docs/wiki/`
4. Bootstraps **`AGENTS.md`** with instructions to read that wiki
5. Supports **incremental updates** via git change detection (`.agentwiki/last-run.json`)

It is intentionally file-based Markdown (not a RAG vector DB). Spec source of truth: `AgentWiki-Project-Specification.md`.

---

## 2. How to run (day-to-day)

```bash
# From repo root
dotnet build AgentWiki.slnx
dotnet test AgentWiki.slnx

# Install/update global tool
./scripts/pack-and-install-tool.sh
agent-wiki --version

# On a target repo (example: LMS LoanView)
agent-wiki init --repo-path /path/to/repo
agent-wiki test-provider --repo-path /path/to/repo
agent-wiki generate --repo-path /path/to/repo --force
agent-wiki update --repo-path /path/to/repo
agent-wiki status --repo-path /path/to/repo --analyze
```

**Logs (always):** `~/.agentwiki/logs/agent-wiki-YYYYMMDD.log`  
Shown on `status`, start of `generate`/`update`, and on errors. Console is Spectre-only by default; use `--verbose` to also stream diagnostics.

---

## 3. Architecture (code map)

| Layer | Path | Role |
|-------|------|------|
| Core models | `src/AgentWiki.Core/Models/` | Config, wiki docs, generation results |
| Core analysis | `src/AgentWiki.Core/Analysis/` | Gitignore, categorization, summary, prompt truncation |
| Core generation | `src/AgentWiki.Core/Generation/` | Markdown renderers, offline planners, flexible LLM JSON |
| **App services** | `src/AgentWiki.App/Services/` | Analyzer, SK LLM, orchestrator, git, bootstrap (shared) |
| App DI | `src/AgentWiki.App/ServiceCollectionExtensions.cs` | `AddAgentWikiServices()` |
| Prompts | `src/AgentWiki.App/Prompts/` | Embedded defaults; repo can override via `.agentwiki/prompts/` |
| Logging | `src/AgentWiki.App/Infrastructure/AgentWikiLogging.cs` | File logs under `~/.agentwiki/logs` |
| CLI entry | `src/AgentWiki.Cli/Program.cs` | Thin Spectre host → App DI |
| Commands | `src/AgentWiki.Cli/Commands/` | `init`, `generate`, `update`, `status`, `test-provider` |
| Desktop | `src/AgentWiki.Desktop/` | Avalonia 12 MVVM companion (full CLI parity) |
| Tests | `tests/AgentWiki.Cli.Tests/`, `tests/AgentWiki.Desktop.Tests/` | xUnit + Shouldly + Moq |
| Skills | `.grok/skills/bump-version/` | Version bump skill + script |
| Pack script | `scripts/pack-and-install-tool.sh` | Pack/install `agent-wiki` + `agent-wiki-ui` (`--cli-only` / `--desktop-only`) |
| Desktop run | `scripts/run-desktop.sh` or `agent-wiki-ui` | Avalonia companion (source or global tool) |

**Hosts:** CLI (CI/automation) and Desktop (interactive) both call **AgentWiki.App**. Do not put Spectre or Avalonia in App.

### Generation pipeline

```
RepoAnalyzer
  → (update only) GitChangeDetector + IncrementalScope
  → WikiGenerationOrchestrator
       1. ArchitectureGenerator (LLM or offline)
       2. Module plan (LLM full runs / offline)
       3. Module pages (per module, LLM or offline)
       4. Cross-cutting pages
       5. Index + support pages (key-components, data-flows, inventory, glossary, getting-started)
  → MarkdownOutputWriter
  → AgentBootstrapper (AGENTS.md)
  → LastRunStore (.agentwiki/last-run.json)
```

### Config priority (highest wins)

1. CLI flags  
2. Repo-root `.env`  
3. `.agentwiki/config.json`  
4. Process env vars `AGENTWIKI_*` (and nested `__` keys)  
5. Tool `appsettings.json`  

**Secrets** → `.env` or CI secrets. **Non-secrets** → `config.json` (or env when convenient).

All LLM settings support env vars, e.g. `AGENTWIKI_OpenAI__Endpoint`, `AGENTWIKI_OpenAI__ApiKey`, `AGENTWIKI_OpenAI__Model`, `AGENTWIKI_AzureOpenAI__Endpoint`, `AGENTWIKI_AzureOpenAI__DeploymentName`, `AGENTWIKI_AzureOpenAI__ApiKey`, `AGENTWIKI_LlmTimeoutSeconds`.

Key settings: `provider`, `defaultModel`, `openAI.*`, `azureOpenAI.*`, `llmTimeoutSeconds` (default 300), `maxLlmSummaryChars` (default 16000), `maxFilesToAnalyze`, `ignorePatterns`.

**Paths:** CLI expands `~` to the user home directory. Generated wiki Markdown uses **repo-relative** paths only.

---

## 4. Important product decisions (do not re-litigate casually)

| Decision | Rationale |
|----------|-----------|
| Spectre owns the terminal | Serilog **file-only** by default so spinners stay clean |
| Offline fallback always available | Enterprise repos must still get a wiki without LLM |
| Structured JSON from LLM | Reliability; parsers must tolerate free-form shapes |
| No temperature by default | Many modern models reject sampling params |
| JSON prompts must include the word `json` | OpenAI `response_format=json_object` requirement |
| Incremental updates via git | CI-friendly; filters wiki/agent noise |

---

## 5. Recent bugs fixed (context for “why is the code like this?”)

| Version | Fix |
|---------|-----|
| 1.0.1 | OpenAI scaffold empty `{}` on init; temperature omitted; `test-provider` command |
| 1.0.2 | HttpClient timeout 100s → configurable 300s; truncate LLM summaries; don’t retry timeouts |
| 1.0.3 | Logs → `~/.agentwiki/logs`; no Info on console |
| 1.0.4 | Auto-append “JSON” to messages for `json_object` format |
| 1.0.5 | Flexible LLM JSON (`purpose` object, `dependencies` object → strings) |
| 1.0.6 | Accept `{ "architecture_overview": "# markdown..." }` as full architecture page |
| 1.0.7 | Handoff docs; anti-deprecation prompt rules; cleaner index/disclaimer language |
| 1.0.8 | Fix config.json `llmTimeoutSeconds` merge; `.env` > config > process env priority; full LLM env vars; `~` path expansion; portable wiki paths; no index truncation; step progress console UX; Policies/pipeline analysis boost |
| 1.0.9 | Effective model uses `defaultModel` when nested model empty; status shows sources/timeout default tip; empty nested models in appsettings |
| 1.0.10 | config.json merge only applies present JSON properties (no longer resets process-env timeout to class default 300) |

### Known remaining polish (as of 1.0.8)

- Module `dependencies` can still look noisy when models return deep objects (flattening improved but not perfect).
- `gpt-chat-latest` often ignores our strict JSON schema and returns free-form fields — parsers must stay tolerant.
- Target repos with old `.agentwiki/prompts/` may need `init --force` to pick up newer sample prompts.
- LLM-authored prose may still invent absolute paths occasionally; prompts instruct relative paths.

---

## 6. Versioning

Always keep these in sync:

- `Directory.Build.props` (`Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`)
- `src/AgentWiki.Core/Constants/AgentWikiConstants.cs` (`Version` const)

```bash
# Skill script
./.grok/skills/bump-version/scripts/bump-version.sh patch   # or minor|major|X.Y.Z

# Then pack/install
./scripts/pack-and-install-tool.sh
```

Slash skill: `/bump-version` (project skill under `.grok/skills/bump-version/`).

---

## 7. Testing expectations

```bash
dotnet test AgentWiki.slnx
```

Prefer offline unit tests (no live LLM in CI). Mock `ILlmCompletionService` for parse/orchestrator tests. Integration test: `tests/.../Integration/EndToEndOfflineTests.cs`.

When changing LLM parsing, add fixtures under `tests/AgentWiki.Cli.Tests/Generation/`.

---

## 8. Target-repo layout (what `init` creates)

```
.agentwiki/
  config.json
  prompts/          # optional overrides of embedded prompts
  .gitignore        # ignores last-run.json
  last-run.json     # after first successful generate/update (local)
.env.example        # copy to .env for secrets
docs/wiki/          # generated output
AGENTS.md           # bootstrap block (or CLAUDE.md if present)
```

---

## 9. Suggested next work (if continuing product)

1. Desktop polish: richer Markdown render, multi-repo job queue, dry-run diff view  
2. Optional structured-output schemas / stricter tool-calling if models support it  
3. Richer cost/token usage when provider returns usage (already partially shown)  
4. Azure DevOps pipeline sample parity with GitHub Actions  
5. Post-process LLM output to strip accidental absolute paths  
6. When ready: push nupkg to Azure Artifacts (not NuGet.org) from CI; desktop remains side-car publish  

### Desktop companion (implemented 2026-07-10)

Plan: `docs/plans/ui-companion-avalonia.md` (status: implemented for v1 parity).

- `AgentWiki.App` extracted from Cli services; CLI thin host; tests green.
- Avalonia **12.1** desktop: Dashboard, Generate, Update, Setup, Settings, Provider, Wiki, Logs.
- Progress via `WikiGenerationRequest.Progress`; cancel via `CancellationToken`.
- UI prefs: `~/.agentwiki/ui-settings.json` (recent repos, last path).
- Secrets: Settings saves keys to `.env` only; non-secrets to `config.json`.
- **Tool packaging:** separate nupkg `AgentWiki.Desktop` → global command **`agent-wiki-ui`** (not in the CLI package).

```bash
./scripts/pack-and-install-tool.sh --desktop-only
agent-wiki-ui
# or from source
./scripts/run-desktop.sh
```

### CI (this repo)

| Workflow | Role |
|----------|------|
| `.github/workflows/ci.yml` | Build, test, pack CLI + Desktop nupkgs as artifacts; smoke-install both tools (no GUI launch) |
| `.github/workflows/wiki-refresh.yml` | Offline dogfood wiki PR (weekly / manual) |
| `examples/github-actions/agent-wiki-update.yml` | **Consumer template** — copy into other repos |

---

## 10. Files a new agent should read first

1. **This file** — `docs/HANDOFF.md`  
2. `README.md` — user-facing product docs  
3. `AGENTS.md` — agent bootstrap for *this* repo  
4. `CONTRIBUTING.md` — how to extend  
5. `AgentWiki-Project-Specification.md` — original product spec  
6. `src/AgentWiki.App/ServiceCollectionExtensions.cs` + `WikiGenerationOrchestrator.cs` — shared control flow  
7. `src/AgentWiki.Cli/Program.cs` / `src/AgentWiki.Desktop/` — hosts  
8. Latest log: `~/.agentwiki/logs/` if debugging a run  
9. **UI companion plan:** `docs/plans/ui-companion-avalonia.md` (v1 implemented)

---

## 11. One-liner for a new conversation

> Continue AgentWiki (.NET 10, v1.1.0): Core + App services + thin Spectre CLI (`agent-wiki`) + Avalonia 12 Desktop tool (`agent-wiki-ui`); generates agent-optimized Markdown wikis via RepoAnalyzer + Semantic Kernel multi-step pipeline, offline fallback, git incremental updates, logs at `~/.agentwiki/logs`. CLI stays primary for CI. Read `docs/HANDOFF.md`, then fix product issues without re-scaffolding the solution.
