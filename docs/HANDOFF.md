# AgentWiki — session handoff (for new conversations)

**Last updated:** 2026-07-21  
**Current version:** **1.5.0**  
**Repo:** this repository root  
**Active plan:** Step 02b complete — workspace corpus, routing quality & member orchestration (`docs/development/02b-workspace-corpus-routing-requirements.md`)

| Surface | Package | Command |
|---------|---------|---------|
| CLI (CI/agents primary) | `AgentWiki.Cli` | `agent-wiki` |
| Desktop companion | `AgentWiki.Desktop` | `agent-wiki-ui` |

This document is the single best place for a new coding agent or human to continue work without re-deriving session history.

**Session hygiene:** commit after each completed step (product + tests + docs). Do **not** publish to NuGet.org (local pack / Azure Artifacts later).

**Git (as of this handoff):** **1.5.0** on `main` includes Step 02b (workspace corpus + routing; still file-based only).

---

## 1. What this project is

**AgentWiki** is a native **.NET 10** product that:

1. Analyzes a repository (gitignore-aware inventory)
2. Optionally calls an LLM (Semantic Kernel + OpenAI / Azure OpenAI / GitHub Models)
3. Writes an **agent-optimized wiki** under `docs/wiki/`
4. Produces / maintains **`AGENTS.md`** (full file when missing; bootstrap block when rich) and optional **README.md**
5. Supports **incremental updates** via git change detection (`.agentwiki/last-run.json`)
6. **Workspace mode (1.5+):** multi-repo **routing corpus** under `docs/knowledge-base/` with member manifests, web deep links, and Phase 2–ready meta (still no vectors)

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
agent-wiki --version                # 1.5.0
agent-wiki-ui

agent-wiki init --repo-path /path/to/repo
agent-wiki test-provider --repo-path /path/to/repo
agent-wiki generate --repo-path /path/to/repo --force
agent-wiki agents --repo-path /path/to/repo
agent-wiki update --repo-path /path/to/repo
agent-wiki status --repo-path /path/to/repo --analyze

# Multi-repo workspace (Step 02b — corpus + routing)
agent-wiki workspace init "My System" --repo-path /path/to/workspace-root
agent-wiki workspace add ../ServiceA --repo-path /path/to/workspace-root
agent-wiki workspace member replace-configs --force
agent-wiki workspace generate --repo-path /path/to/workspace-root
agent-wiki workspace generate --update-members=stale
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
| Shared constants | `src/AgentWiki.Core/Constants/Constants.cs` → `Constants.Product\|Paths\|WorkspaceManifest\|Workspace\|…` |
| Full AGENTS.md | `IAgentsMdGenerator` / `AgentsMdGenerator`, `AgentsMdOfflineBuilder` |
| README | `IReadmeGenerator` / `ReadmeGenerator`, `ReadmeHeuristics`, `ReadmeOfflineBuilder` |
| Bootstrap block only | `AgentBootstrapper` (rich AGENTS.md still uses this) |
| Generate wiring | `SemanticWikiGenerator.ApplyAgentsAndReadmeAsync` + manifest scaffold |
| **Workspace corpus (02b)** | `WorkspaceManifestParser/Scaffold`, `AgentWikiConfigDefaults`, `MemberConfigApplier`, `MemberWikiFreshness`, `RepoWebLinkBuilder`, `WorkspaceOfflineBuilder` (routing cards), `WorkspaceOrchestrator` |
| Workspace CLI | `WorkspaceCommands` + `workspace member replace-configs` in `Program.cs` |
| Step docs | `docs/development/01-*.md`, `02-multi-repo-workspace-phase1-*.md`, `02b-workspace-corpus-routing-*.md` |

---

## 4. What landed recently

### Init defaults + fail-loud LLM (1.5.1+)

- **`agent-wiki init`** writes **minimal** `.agentwiki/config.json` (`provider`, `defaultModel`, `outputPath` only)
- Full property surface → **`.agentwiki/config.example.json`**
- Product defaults: **`openai`** / **`gpt-chat-latest`** (was azure-openai / gpt-4o)
- API key via **`OPENAI_API_KEY`** (global env), process `AGENTWIKI_*`, or repo `.env` — not scaffolded into config.json
- Still writes `.env.example`, sample prompts, `.agentwiki/.gitignore`
- **`allowOfflineFallback` default is `false`**: live LLM transport/parse failures fail the run (no silent inventory-only wiki). Opt in with `allowOfflineFallback: true` or `AGENTWIKI_AllowOfflineFallback=true`.
- **Live providers require LLM**: for `openai` / `azure-openai` / `github-models`, missing credentials **fail** with a clear error. Inventory-only generation requires explicit `provider: offline` (or `none` / `mock`).

### v1.5.0 — Step 02b: Workspace corpus, routing & member orchestration

**Still file-based only** — no embeddings, Azure AI Search, or MCP.

- **Manifest:** `docs/wiki/workspace-manifest.md` human-owned (Purpose, rules, Layer, Team, Applications/Services, Brands Rise/Shine/Elastic/Blueprint, routing sections); scaffold on single-repo generate when missing; never overwrite
- **memberDefaults:** full `AgentWikiConfig` template in `workspace.json`; seeded on workspace init; init-copy into members when config missing; **`workspace member replace-configs`** force overwrite (dry-run / --id / --force / CI skip confirm)
- **memberWikiPolicy:** `ensureMissing` (default true), `updateMembers` never\|stale\|all (default never); CLI `--update-members`, `--no-ensure-member-wikis`
- **Staleness:** git HEAD vs baseline (member last-run commit → workspace last-run head); calendar age is soft warning only
- **Ids:** exact repo name (case/dots preserved); collision `-2`, `-3`
- **Web links:** GitHub + Azure DevOps from upstream/origin + current branch
- **Corpus:** `routing-guide.md`, `members/<id>/index.md` routing cards, Phase 2 meta JSON (layer, brands, apps, repoUrl, wikiWebUrl, …)
- **Offline-first** system synthesis; dry-run never writes; single-repo mode preserved
- **Tests:** manifest parser, defaults/replace-configs, freshness policy, link builder, corpus, workspace offline E2E

### v1.4.x — Step 02: Multi-repo file-based Workspace (Phase 1)

- workspace init/add/list/remove/generate/update/status; system KB scaffold; remote cache

### v1.3.0 — Step 01: Full AGENTS.md + README generation

---

## 5. Recommended next steps

1. Pack/install: `./scripts/pack-and-install-tool.sh` → verify `agent-wiki --version` is **1.5.0**
2. Consumer trial: multi-repo workspace, fill member manifests (layer/brands/apps), `workspace generate`
3. **Phase 2 (vectors / Azure AI Search / shared HTTP MCP)** — GitHub issue #2 — index `docs/knowledge-base/**` + meta; do **not** add embeddings until then
4. Optional: `workspace member status|init|generate|update` CLI polish; Desktop Workspace tab
5. Optional: LLM enrichment path for system architecture pages (offline routing cards remain authoritative for human fields)

---

## 6. Do not

- Publish to NuGet.org without explicit request  
- Force-push / rewrite shared history  
- Commit secrets or `.env` with keys (warn if memberDefaults contains apiKey)  
- Break offline generate when LLM is unavailable  
- Overwrite rich AGENTS.md / README without force / generic detection  
- Overwrite existing `workspace-manifest.md`  
- Implement vector/embedding/RAG/MCP until Phase 2 requirements are explicit  
- Break single-repo `generate` / `update` / `agents` behavior when changing workspace code  
