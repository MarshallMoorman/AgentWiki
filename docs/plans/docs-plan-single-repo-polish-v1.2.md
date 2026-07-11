# AgentWiki Single-Repo Polish Plan (v1.2)

**Status:** Ready for implementation  
**Target version:** 1.2.x  
**Date:** 2026-07-10  
**Scope:** Finish and harden single-repo mode only. Multi-repo Workspace and Vector work are intentionally deferred (see existing GitHub issues).  
**Desktop note:** Most Desktop polish already exists. Only dark/light theme switching is remaining and is included as a small item.  
**Publishing:** Explicitly out of scope. A separate GitHub issue is provided for future packaging/publishing work.

---

## Goals

Ship a high-quality, production-ready single-repo AgentWiki experience by implementing the following improvements:

1. Richer Offline Quality (including Roslyn-based .NET analysis)
2. Post-Processing / Guardrails for LLM Output
3. Azure DevOps Pipeline Sample
4. Cost & Observability Polish
5. Module Discovery Improvements
6. Desktop Theme Switching (dark/light)
7. **New:** API Endpoint Documentation (App Service / Azure Functions / ASP.NET controllers & minimal APIs)

All work must preserve:
- Offline-first behavior
- Existing CLI surface and config layering
- Clean Core / App / thin hosts architecture
- Strong tests
- Ability to commit cleanly after each phase

---

## Non-Goals (this plan)

- Multi-repo Workspace (separate issue)
- Vector / Azure AI Search (separate issue)
- Publishing to NuGet.org or Azure Artifacts
- Major Desktop UX overhauls beyond theme switching
- Changing the core multi-step generation pipeline shape (architecture → modules → cross-cutting → support pages)

---

## Detailed Feature Breakdown

### 1. Richer Offline Quality + Roslyn Enrichment

**Why:** Offline mode is already useful but still inventory-heavy. Better heuristics + real .NET static analysis will make offline wikis dramatically more valuable for Elevate’s primarily .NET estate.

**Work:**
- Introduce optional Roslyn-based analysis for C#/.NET projects (when the repo contains .csproj / .sln).
- Detect and surface:
  - Public types / key classes / interfaces
  - Controllers, Minimal API endpoints, Azure Function triggers
  - DI registration patterns (if easily detectable)
  - Common attributes (`[Obsolete]`, `[ApiController]`, etc.)
  - Entry points (Program.cs, Startup, Function apps)
- Improve offline architecture and module page generation:
  - Better “Purpose”, “Key Types”, “Entry Points”, “How to Extend”, “Gotchas”
  - Prefer real symbols over pure path heuristics
- Keep Roslyn completely optional and offline-safe (graceful fallback if Roslyn fails or repo is not .NET).
- Performance: only analyze a reasonable subset of projects/files (configurable).

**New/Changed components:**
- `IStaticAnalyzer` abstraction
- `RoslynStaticAnalyzer` (new)
- Updates to `OfflineArchitectureGenerator`, `OfflineModulePlanner`, and related renderers
- Config knobs: `enableRoslynAnalysis`, `maxProjectsToAnalyze`, etc.

### 2. Post-Processing / Guardrails for LLM Output

**Why:** LLMs still occasionally invent absolute paths, free-form dependency objects, deprecation language, or broken links.

**Work:**
- Add a dedicated post-processing / lint pipeline that runs after every LLM (and offline) generation step:
  - Strip or rewrite absolute paths → repo-relative
  - Normalize free-form dependency objects into clean string lists
  - Detect and neutralize invented deprecation/legacy language unless `[Obsolete]` or similar markers exist in source
  - Validate relative Markdown links (warn or auto-fix obvious breaks)
  - Ensure consistent heading structure and front-matter if used
- Make the post-processor configurable (strict vs lenient).
- Log all corrections for observability.
- Unit tests with real messy LLM fixtures.

**New/Changed components:**
- `IWikiPostProcessor` / `WikiPostProcessor`
- Integration into `WikiGenerationOrchestrator` and `SemanticWikiGenerator`
- Expanded test fixtures under `tests/.../Generation/`

### 3. Azure DevOps Pipeline Sample

**Why:** Elevate uses Azure DevOps heavily. GitHub Actions example already exists; parity is needed.

**Work:**
- Create a high-quality, copy-paste ready Azure Pipelines YAML example (similar quality to the existing GitHub Actions consumer template).
- Place it under `examples/azure-pipelines/agent-wiki-update.yml` (or similar).
- Document how to use it (variables, secrets, schedule, PR creation, offline vs live LLM).
- Update README and HANDOFF.

### 4. Cost & Observability Polish

**Why:** Users need better visibility into token usage, estimated cost, and run health.

**Work:**
- Improve token usage capture and aggregation (already partially present).
- Surface estimated cost more prominently when the provider returns usage (or use configurable pricing tables).
- Enhanced `status` command and generation summary tables.
- Optional Application Insights sink (config-driven, off by default).
- Better dry-run mode: show a clear summary of what *would* change (files added/updated) without writing.
- Correlation ID and step timing already exist — make them more visible in logs and CLI output.
- Desktop: ensure the same summary data appears in the Generate/Update results pane.

### 5. Module Discovery Improvements

**Why:** Hard cap of 8 modules + pure path heuristics is limiting for real monorepos and multi-project solutions.

**Work:**
- Smarter module discovery:
  - Prefer .sln / .csproj structure
  - Group by solution folders, project type (web, classlib, test, function), or naming conventions
  - Allow config-driven `moduleRoots` or `moduleGlobs` in `.agentwiki/config.json`
- Raise or make configurable the LLM module limit (with good offline fallback).
- Better module IDs and titles.
- Improve how related files are associated with modules.
- Update offline planner and LLM module-plan prompt accordingly.

### 6. Desktop Theme Switching (Dark / Light)

**Why:** Only remaining Desktop polish item called out.

**Work:**
- Add proper dark/light theme switching (Fluent theme support already present).
- Persist preference in `~/.agentwiki/ui-settings.json`.
- Follow system theme by default with manual override.
- Ensure Markdown preview, logs, and all views look good in both themes.
- Minimal code change — do not expand Desktop scope beyond this.

### 7. API Endpoint Documentation (New – Recommended)

**Why:** Extremely high value for agent-optimized docs. Agents constantly need to know “what HTTP endpoints does this service expose?”. Fits naturally with Roslyn analysis and module pages.

**Work:**
- When analyzing ASP.NET Core / Azure Functions / minimal API projects, extract and document public endpoints:
  - Route, HTTP method(s), controller/action or function name
  - Parameters (route/query/body) where easily available
  - Auth attributes if present
- Surface them in:
  - A new top-level `api-endpoints.md` (or `endpoints.md`) page
  - Per-module pages under “Endpoints” or “Public API”
  - Key-components page
- Offline path: Roslyn + attribute scanning (or simple source regex as fallback).
- LLM path: enrich with descriptions / purpose when live LLM is available.
- Configurable (include/exclude patterns, only public, etc.).

**Decision:** Include this. It is a natural and high-leverage extension of richer offline quality + module discovery. If it starts to balloon, we can split it, but it should be part of this plan.

---

## Implementation Phases (Commit After Each)

### Phase 1 – Foundation & Guardrails
- Introduce `IWikiPostProcessor` and basic path/dependency/deprecation normalization.
- Wire it into the orchestrator.
- Add solid unit tests with messy fixtures.
- Update HANDOFF / AGENTS.md notes.

**Exit criteria:** Post-processor runs, absolute paths are cleaned, tests pass, commit.

### Phase 2 – Richer Offline + Roslyn
- Add `IStaticAnalyzer` + `RoslynStaticAnalyzer`.
- Improve offline architecture & module generation with real symbols.
- Config knobs + graceful fallback.
- Tests (including non-.NET repos still work).

**Exit criteria:** Offline wiki quality is noticeably better on .NET solutions, tests green, commit.

### Phase 3 – API Endpoint Documentation
- Extract and document endpoints (controllers, minimal APIs, Functions).
- New `api-endpoints.md` + integration into modules / key-components.
- Offline + LLM enrichment paths.
- Tests.

**Exit criteria:** Endpoint pages appear and are useful, commit.

### Phase 4 – Module Discovery Improvements
- Smarter module detection + config-driven roots.
- Better limits and association of files.
- Update prompts and offline planner.
- Tests.

**Exit criteria:** Better modules on multi-project solutions, commit.

### Phase 5 – Cost, Observability & Dry-Run
- Improved token/cost reporting.
- Better dry-run summary of changes.
- Optional App Insights sink (basic).
- Enhanced status and generation summaries (CLI + Desktop).
- Tests / docs.

**Exit criteria:** Users have clear visibility into cost and what a run will do, commit.

### Phase 6 – Azure DevOps Sample + Desktop Theme + Docs Polish
- Azure Pipelines example + documentation.
- Dark/light theme switching in Desktop + persistence.
- Final README / HANDOFF / CONTRIBUTING / AGENTS.md updates.
- Full regression test pass.
- Version bump preparation notes.

**Exit criteria:** Plan complete, everything documented, ready for 1.2 release tag when desired.

---

## Technical Guidelines

- Follow existing architecture: Core (models, analysis, generation helpers) → App (services) → thin Cli / Desktop hosts.
- Prefer primary constructors, file-scoped namespaces, nullable enabled, XML docs on public types.
- Offline path must always work and be tested.
- Never log secrets or full prompt/response bodies by default.
- All new config must support the existing priority order (CLI > .env > config.json > process env > appsettings).
- Keep Roslyn and any heavy analysis behind feature flags / config so non-.NET repos stay fast.
- After each phase: update relevant docs (especially HANDOFF.md) and ensure `dotnet test` is green before committing.

---

## Success Metrics for v1.2

- Offline wiki on a real multi-project .NET solution is significantly more useful (real types, endpoints, better structure).
- LLM output rarely contains absolute paths or invented deprecation language.
- Users can see token/cost estimates and dry-run impact clearly.
- Azure DevOps users have a first-class example.
- Desktop has proper theme switching.
- API endpoints are documented automatically for web/function projects.
- No regressions in existing single-repo commands or offline behavior.
- Clean, reviewable commits after each phase.

---

## Out of Scope (captured elsewhere)

- Multi-repo Workspace (existing GitHub issue)
- Vector / Azure AI Search (existing GitHub issue)
- Publishing / Azure Artifacts (separate issue provided)

---

## Notes for Implementers (Grok Build)

- This plan supersedes the earlier high-level suggestions for single-repo polish.
- Treat the phases as hard commit points.
- Prefer small, focused PRs/commits that keep the main branch green.
- When in doubt about scope, favor the offline + agent-value path.
- After the final phase, prepare a short release note summary for 1.2.
