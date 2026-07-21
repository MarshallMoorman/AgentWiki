# AgentWiki — session handoff (for new conversations)

**Last updated:** 2026-07-20  
**Current version:** **1.4.0**  
**Repo:** this repository root  
**Active plan:** Step 02 complete — multi-repo file-based Workspace Phase 1 (`docs/development/02-multi-repo-workspace-phase1-requirements.md`)

| Surface | Package | Command |
|---------|---------|---------|
| CLI (CI/agents primary) | `AgentWiki.Cli` | `agent-wiki` |
| Desktop companion | `AgentWiki.Desktop` | `agent-wiki-ui` |

This document is the single best place for a new coding agent or human to continue work without re-deriving session history.

**Session hygiene:** commit after each completed step (product + tests + docs). Do **not** publish to NuGet.org (local pack / Azure Artifacts later).

**Git (as of this handoff):** **1.4.0** on `main` includes Step 02 (workspace multi-repo Phase 1, file-based only).

---

## 1. What this project is

**AgentWiki** is a native **.NET 10** product that:

1. Analyzes a repository (gitignore-aware inventory)
2. Optionally calls an LLM (Semantic Kernel + OpenAI / Azure OpenAI / GitHub Models)
3. Writes an **agent-optimized wiki** under `docs/wiki/`
4. Produces / maintains **`AGENTS.md`** (full file when missing; bootstrap block when rich) and optional **README.md**
5. Supports **incremental updates** via git change detection (`.agentwiki/last-run.json`)
6. **Workspace mode (1.4+):** multi-repo system knowledge base under `docs/knowledge-base/` with deep links into member wikis (file-based; no vectors)

It is intentionally file-based Markdown (not a RAG vector DB). Spec source of truth: `AgentWiki-Project-Specification.md`.

**Two hosts, one engine:**

- **CLI** — automation, CI, scripts  
- **Desktop** — Avalonia 12 interactive UI  

Both call **`AgentWiki.App`** services (never put Spectre or Avalonia in App).

---

## 2. How to run (day-to-day)

```bash
dotnet build AgentWiki.slnx
dotnet test AgentWiki.slnx

./scripts/pack-and-install-tool.sh
agent-wiki --version                # 1.4.0
agent-wiki-ui

agent-wiki init --repo-path /path/to/repo
agent-wiki test-provider --repo-path /path/to/repo
agent-wiki generate --repo-path /path/to/repo --force
agent-wiki agents --repo-path /path/to/repo
agent-wiki update --repo-path /path/to/repo
agent-wiki status --repo-path /path/to/repo --analyze

# Multi-repo workspace (Phase 1 — file-based system KB)
agent-wiki workspace init "My System" --repo-path /path/to/workspace-root
agent-wiki workspace add svc-a ../ServiceA --repo-path /path/to/workspace-root
agent-wiki workspace generate --repo-path /path/to/workspace-root --force
agent-wiki workspace update --repo-path /path/to/workspace-root
agent-wiki workspace status --repo-path /path/to/workspace-root
```

**Logs:** `~/.agentwiki/logs/agent-wiki-YYYYMMDD.log`  
**Remote member cache:** `~/.agentwiki/cache/workspaces/<workspace-key>/<member-id>/`

---

## 3. Architecture (code map)

```
AgentWiki.slnx
├── src/AgentWiki.Core      # models, analysis, generation helpers, Constants, abstractions
├── src/AgentWiki.App       # services + AddAgentWikiServices()
├── src/AgentWiki.Cli       # thin Spectre host → PackAsTool agent-wiki
├── src/AgentWiki.Desktop   # Avalonia 12 MVVM → PackAsTool agent-wiki-ui
└── tests/
    ├── AgentWiki.Cli.Tests
    └── AgentWiki.Desktop.Tests
```

| Hotspot | Path |
|---------|------|
| Shared constants | `src/AgentWiki.Core/Constants/Constants.cs` → `Constants.Product\|Paths\|Config\|…` |
| Full AGENTS.md | `IAgentsMdGenerator` / `AgentsMdGenerator`, `AgentsMdOfflineBuilder` |
| README | `IReadmeGenerator` / `ReadmeGenerator`, `ReadmeHeuristics`, `ReadmeOfflineBuilder` |
| Bootstrap block only | `AgentBootstrapper` (rich AGENTS.md still uses this) |
| Generate wiring | `SemanticWikiGenerator.ApplyAgentsAndReadmeAsync` |
| **Workspace (Phase 1)** | `WorkspaceConfig` models, `WorkspaceConfigLoader`, `WorkspaceMemberResolver`, `CrossRepoSignalCollector`, `WorkspaceOfflineBuilder`, `WorkspaceOrchestrator`, `WorkspaceLastRunStore` |
| Workspace CLI | `WorkspaceCommands` branch under `workspace` in `Program.cs` |
| Step docs | `docs/development/01-*.md`, `docs/development/02-multi-repo-workspace-phase1-*.md` |

---

## 4. What landed recently

### v1.4.0 — Step 02: Multi-repo file-based Workspace (Phase 1)

**Strictly file-based** — no embeddings, Azure AI Search, or RAG.

- **Config:** `.agentwiki/workspace.json` with local `path` and/or remote `remote`+`branch` members; validation (ids, sources, caps).
- **CLI:** `workspace init | generate | update | status | add`
  - `generate` resolves members, ensures per-repo wikis (reuses `IWikiGenerator`), collects cross-repo signals, writes system KB + workspace `AGENTS.md`
  - `update` incremental via `.agentwiki/workspace-last-run.json` (per-member HEAD / wiki freshness)
  - Remote members: shallow clone/fetch under `~/.agentwiki/cache/workspaces/…`
- **System output** (default `docs/knowledge-base/`): `index.md`, `architecture.md`, `dependency-graph.md`, `data-flows.md`, `ownership.md`, `members/<id>.md` (deep links into member `docs/wiki/`), meta JSON
- **Signals:** PackageReference / packages.config / package.json, ProjectReference cross-matches, CODEOWNERS, OpenAPI/proto/contract paths
- **AGENTS.md (workspace):** “start at root → drill into members” + marker block + mandatory self-update section
- **Offline-first:** `WorkspaceOfflineBuilder`; dry-run never writes; single-repo commands untouched
- **Tests:** config/loader/resolver/signals/offline builder + workspace offline E2E (generate, dry-run, incremental, status, add)
- **Desktop:** no Workspace tab yet (acceptable follow-up)

### Prior — post-1.3.0 quality (module endpoints, README, LLM cleanup)

- Endpoint scoping, noise filter, route token expansion, module page cleanup, richer offline README

### v1.3.0 — Step 01: Full AGENTS.md + README generation

- `agent-wiki agents`, full AGENTS when missing/trivial, README heuristics, copilot migration, self-update section

### Prior (1.2.x)

- Single-repo polish Phases 1–6, CanExecute UI fix, nested `Constants` hierarchy, LLM timeout/retry hardening, blue theme.

---

## 5. Recommended next steps

1. Pack/install tools: `./scripts/pack-and-install-tool.sh` → verify `agent-wiki --version` is **1.4.0**
2. Consumer trial: multi-repo workspace with 2–3 local members → `workspace generate --force`
3. **Phase 2 (vectors / semantic “which repos for this story?”)** when requirements land (GitHub issue #2) — do **not** add embeddings until then
4. Optional: Desktop Workspace tab / member list parity
5. Optional: Desktop parity for `agents` / README messaging

---

## 6. Do not

- Publish to NuGet.org without explicit request  
- Force-push / rewrite shared history  
- Commit secrets or `.env` with keys  
- Break offline generate when LLM is unavailable  
- Overwrite rich AGENTS.md / README without force / generic detection  
- Implement vector/embedding/RAG in this codebase until Phase 2 requirements are explicit  
- Break single-repo `generate` / `update` / `agents` behavior when changing workspace code  
