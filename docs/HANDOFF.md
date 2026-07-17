# AgentWiki — session handoff (for new conversations)

**Last updated:** 2026-07-17  
**Current version:** **1.3.0**  
**Repo:** this repository root  
**Active plan:** Step 01 complete — full AGENTS.md + README generation (`docs/development/01-agents-readme-generation-requirements.md`)

| Surface | Package | Command |
|---------|---------|---------|
| CLI (CI/agents primary) | `AgentWiki.Cli` | `agent-wiki` |
| Desktop companion | `AgentWiki.Desktop` | `agent-wiki-ui` |

This document is the single best place for a new coding agent or human to continue work without re-deriving session history.

**Session hygiene:** commit after each completed step (product + tests + docs). Do **not** publish to NuGet.org (local pack / Azure Artifacts later).

**Git (as of this handoff):** **1.3.0** on `main` includes Step 01 (full AGENTS.md / README generation).

---

## 1. What this project is

**AgentWiki** is a native **.NET 10** product that:

1. Analyzes a repository (gitignore-aware inventory)
2. Optionally calls an LLM (Semantic Kernel + OpenAI / Azure OpenAI / GitHub Models)
3. Writes an **agent-optimized wiki** under `docs/wiki/`
4. Produces / maintains **`AGENTS.md`** (full file when missing; bootstrap block when rich) and optional **README.md**
5. Supports **incremental updates** via git change detection (`.agentwiki/last-run.json`)

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
agent-wiki --version                # 1.3.0
agent-wiki-ui

agent-wiki init --repo-path /path/to/repo
agent-wiki test-provider --repo-path /path/to/repo
agent-wiki generate --repo-path /path/to/repo --force
agent-wiki agents --repo-path /path/to/repo
agent-wiki update --repo-path /path/to/repo
agent-wiki status --repo-path /path/to/repo --analyze
```

**Logs:** `~/.agentwiki/logs/agent-wiki-YYYYMMDD.log`

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
| Shared constants | `src/AgentWiki.Core/Constants/Constants.cs` → `Constants.Product|Paths|Config|…` |
| Full AGENTS.md | `IAgentsMdGenerator` / `AgentsMdGenerator`, `AgentsMdOfflineBuilder` |
| README | `IReadmeGenerator` / `ReadmeGenerator`, `ReadmeHeuristics`, `ReadmeOfflineBuilder` |
| Bootstrap block only | `AgentBootstrapper` (rich AGENTS.md still uses this) |
| Generate wiring | `SemanticWikiGenerator.ApplyAgentsAndReadmeAsync` |
| CLI | `AgentsCommand`, `GenerateCommand` |
| Step docs | `docs/development/01-agents-readme-generation-*.md` |

---

## 4. What landed recently

### v1.3.0 — Step 01: Full AGENTS.md + README generation

- **`agent-wiki agents`**: complete AGENTS.md from inventory, optional wiki excerpts, and instruction files; offline template + optional LLM polish; `--force`, `--dry-run`, `--with-readme`.
- **Self-updating section (mandatory):** every generated AGENTS.md includes `## Keep this file (and README) up to date`.
- **`generate` / `update`:** when `generateAgentsMdIfMissing` (default true), create **full** AGENTS.md if missing/trivial; otherwise refresh only the AgentWiki marker block via `AgentBootstrapper`.
- **README:** when `generateReadmeIfMissingOrGeneric` (default true), write README if missing or generic; never overwrite rich READMEs.
- **Copilot migration:** incorporate `.github/copilot-instructions.md` (and root variant); **delete after successful write**; dry-run never deletes.
- Config flags + env: `GenerateAgentsMdIfMissing`, `GenerateReadmeIfMissingOrGeneric`, `MigrateCopilotInstructions`, length thresholds.
- Tests: offline builder, heuristics, dry-run, migration delete, generate sparse-repo e2e; bootstrap tests still green.
- Desktop: no dedicated Agents tab yet (follow-up acceptable per requirements).

### Prior (1.2.x)

- Single-repo polish Phases 1–6, CanExecute UI fix, nested `Constants` hierarchy, LLM timeout/retry hardening, blue theme.

---

## 5. Recommended next steps

1. Pack/install tools if not already: `./scripts/pack-and-install-tool.sh`
2. Optional: Desktop parity for `agents` / README messaging
3. Consumer-repo trial: sparse fixture with only copilot-instructions → `generate --force`
4. Continue product roadmap beyond Step 01 when requirements land under `docs/development/`

---

## 6. Do not

- Publish to NuGet.org without explicit request  
- Force-push / rewrite shared history  
- Commit secrets or `.env` with keys  
- Break offline generate when LLM is unavailable  
- Overwrite rich AGENTS.md / README without force / generic detection  
