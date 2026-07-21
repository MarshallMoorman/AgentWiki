# Step 02 — Multi-repo File-based Workspace (Phase 1)

**Project:** AgentWiki  
**Version target:** 1.4.0 (or next minor after current 1.3.x)  
**Date:** 2026-07-20  
**Status:** Ready for implementation  

## Context

Single-repo mode is complete and polished (v1.2 + Step 01: full `AGENTS.md` + smart README generation with self-updating instructions).  
The new `agent-wiki agents`, auto-generation during `generate`, copilot-instructions migration, and `AgentsMdOfflineBuilder` / `ReadmeOfflineBuilder` are in place.

We are now ready for the first phase of multi-repo support. This directly addresses the need for system-level context across hundreds of repositories (e.g., Elevate Credit environment) so that coding agents can understand cross-repo dependencies, ownership, data flows, and — crucially — be guided to the right repos for a given story/requirement.

**This step is intentionally file-based only** (no vectors / embeddings / Azure AI Search). Vector support is deferred to a future Phase 2 (see companion GitHub issue #2).

## Goals

1. Introduce a first-class **Workspace** concept.
2. Allow users to define a workspace grouping multiple related repositories (local paths or remote git repos).
3. Generate a high-level **system / knowledge-base wiki** at the workspace root that aggregates and deep-links into each member repo’s individual `docs/wiki/`.
4. Produce a workspace-level `AGENTS.md` (full file or with the standard marker block) that tells agents to start at the workspace root and drill into member wikis.
5. Reuse as much existing single-repo machinery as possible (`RepoAnalyzer`, `WikiGenerationOrchestrator`, offline planners/builders, `AgentBootstrapper`, incremental `LastRunStore`, config layering, logging, Desktop later, etc.).
6. Keep everything fully file-based, reviewable Markdown, offline-friendly, and git-friendly.
7. Support both local-path workspaces and remote git-based members (with safe shallow cloning / caching).

## Non-Goals (this step)

- Any vector embeddings, Azure AI Search, or RAG layer (Phase 2).
- Full monorepo analysis (workspaces can point at monorepo roots if desired).
- Real-time continuous watching or automatic discovery of *all* company repos.
- Cross-repo semantic code search (focus on wiki-level + high-signal signals).
- Major Desktop UI changes (basic parity or a future Workspace tab is acceptable later).
- Changing existing single-repo commands or behavior.

## Functional Requirements

### A. Workspace Concept & Configuration

A workspace is defined by a JSON config (e.g. `.agentwiki/workspace.json` or `workspaces/<name>.json` at repo root, or a dedicated workspace root).

Example (from planning):

```json
{
  "name": "Lending Core",
  "description": "Core lending platform and related services",
  "outputPath": "docs/knowledge-base",
  "members": [
    {
      "id": "loan-service",
      "path": "../LoanService",
      "label": "Loan Service",
      "role": "service"
    },
    {
      "id": "shared-domain",
      "remote": "https://github.com/org/SharedDomain.git",
      "branch": "main",
      "label": "Shared Domain Models"
    }
  ],
  "ignorePatterns": [],
  "systemPromptOverrides": {}
}
```

- Support local `path` (relative or absolute) and remote git `remote` + optional `branch`.
- Validation on load (duplicate ids, missing paths, invalid remotes, etc.).
- Config loading follows existing priority (CLI args > workspace config > .env > global config).

New config keys (in `AgentWikiConfig` or dedicated `WorkspaceConfig`):
- `WorkspaceConfigPath`, `DefaultWorkspaceOutputPath`, `EnableWorkspaceMode`, etc.

### B. New CLI Surface (`workspace` subcommand)

```bash
agent-wiki workspace init [name]          # scaffold example workspace.json
agent-wiki workspace generate             # full system wiki + member wikis if needed
agent-wiki workspace update               # incremental (only changed members + system pages)
agent-wiki workspace status               # show members, last-run, health, missing wikis
agent-wiki workspace add <id> <path|remote>   # convenience to add a member
```

Options (consistent with existing):
- `--repo-path` / `-r` (the workspace definition lives here)
- `--output` / `-o` (overrides config)
- `--force`, `--dry-run`, `--verbose`
- Model / provider overrides for LLM parts of system generation

`workspace generate` should:
1. Load & validate workspace config + members.
2. For each member: ensure it has a reasonably fresh per-repo wiki (call existing `generate`/`update` logic or clearly error/warn if missing).
3. Collect cross-repo signals (project references, NuGet/packages, HTTP clients, message contracts, OpenAPI, CODEOWNERS, existing architecture docs, etc.).
4. Run multi-step generation (reuse orchestrator) for:
   - System architecture overview
   - Dependency graph (repos + key packages)
   - Cross-repo data flows / contracts / ownership map
   - Index page with deep links into each member’s `docs/wiki/`
5. Write root-level wiki under configured `outputPath`.
6. Create/update workspace-level `AGENTS.md` (full file when appropriate, or marker block) with instructions pointing to the root wiki + member wikis.
7. Update `.agentwiki/last-run.json` (per workspace) for incremental support.

`workspace update` reuses the same incremental logic (`GitChangeDetector`, `LastRunStore` extended to workspace + per-member).

### C. Generation Pipeline & Output Shape (suggested)

Recommended output (workspace root):

```
docs/knowledge-base/          # or docs/wiki/ at workspace root
├── index.md                  # Entry point + navigation to members
├── architecture.md           # System-level design, layers, key decisions
├── dependency-graph.md
├── data-flows.md
├── ownership.md
├── members/
│   ├── loan-service.md       # Summary + link to that repo’s own docs/wiki/
│   └── ...
└── .agentwiki-meta.json
```

Each member summary page must contain clear, relative or documented links into the member repo’s `docs/wiki/index.md`, `architecture.md`, etc.

Reuse existing `WikiGenerationOrchestrator`, `Offline*Planner` / `Offline*Builder`, `AgentsMdOfflineBuilder`, `MarkdownOutputWriter`, etc. as much as possible. Extend where needed (new models in Core, new services in App).

### D. AGENTS.md at Workspace Level

- Workspace root should have (or get) an `AGENTS.md` that tells agents:
  - Start at the workspace root `index.md` / `architecture.md`
  - Drill into specific member wikis for details
  - The self-updating section (already mandatory from Step 01)
- When a member repo already has rich `AGENTS.md`, prefer linking to it rather than duplicating.
- The standard AgentWiki marker block (`<!-- BEGIN AGENTWIKI -->` … `<!-- END AGENTWIKI -->`) must be present so tooling can refresh pointers.

### E. Remote Member Support & Caching

- For remote members: shallow clone (or fetch updates) into a safe cache location (`~/.agentwiki/cache/workspaces/<workspace-id>/<member-id>/` or similar).
- Respect branch / commit pinning if specified.
- Clear error messages and guidance when a member wiki is missing or stale.
- Security: never store secrets in workspace config; use existing `.env` / env-var patterns.

### F. Offline & Incremental Support

- Full offline mode must work (inventory + heuristics + offline builders) exactly like single-repo.
- Incremental updates: track last-run per workspace + per member. Only re-generate system pages when relevant member wikis or cross-repo signals change.
- Reuse / extend `GitChangeDetector`, `LastRunStore`, `.agentwiki/last-run.json`.

### G. CLI / UX / Logging

- New `workspace` subcommand appears in `--help` and Spectre tables.
- Clear, actionable messages: “Member ‘loan-service’ wiki is stale — run `agent-wiki generate` in that repo first”, “Workspace system wiki generated (X pages, Y tokens)”, dry-run summaries, etc.
- Progress reporting for multi-member generation.
- Desktop: acceptable to defer full Workspace tab/mode (repo list can become member list later).

### H. Testing

- Unit tests for new models, config loader, member resolver, cross-signal collector.
- Offline integration tests: workspace with 2–3 local members, generate full system wiki, verify links and AGENTS.md.
- Dry-run + incremental tests.
- Existing single-repo tests must remain green; no breakage to per-repo commands.

### I. Documentation & Polish (mandatory)

After implementation:
- Update `README.md` with `workspace` commands + quick-start example.
- Update `docs/HANDOFF.md` with what was built, current version, and recommended next step (Phase 2 vectors or Desktop parity).
- Update `AGENTS.md` (this repo) only if conventions changed.
- Update `CONTRIBUTING.md` if needed.
- Commit the step requirements + prompt files under `docs/development/`.

## Acceptance Criteria

- [ ] User can define a workspace with 2–10 members (mix of local paths + remote git) via JSON config.
- [ ] `workspace init` scaffolds a sensible example.
- [ ] `workspace generate` produces a useful system-level wiki (`index.md`, `architecture.md`, member summaries with deep links) that agents can start from.
- [ ] Workspace-level `AGENTS.md` is created/updated with correct “start at root → drill into members” guidance + self-updating section.
- [ ] `workspace update` is efficient (only changed members + system pages).
- [ ] Offline mode produces a usable (if less rich) system map.
- [ ] Existing single-repo `generate` / `update` / `agents` commands remain completely unaffected.
- [ ] Good error messages when a member wiki is missing/stale.
- [ ] All tests green (new + existing); dry-run never writes.
- [ ] Version bumped, HANDOFF/README/AGENTS updated, step docs committed.
- [ ] No secrets logged; full offline path remains functional.

## Implementation Notes & Rules for the Implementer

- Follow existing clean architecture: Core (new models, analysis helpers) → App (services: `WorkspaceLoader`, `MemberResolver`, `WorkspaceOrchestrator` or extension of existing) → thin CLI host.
- Prefer primary constructors, records, nullable, modern C#.
- Reuse `RepoAnalyzer`, `SemanticWikiGenerator` / orchestrator patterns, `AgentsMdOfflineBuilder`, `WikiPostProcessor`, incremental stores, config priority, logging, Polly resilience.
- New abstractions where clean (`IWorkspaceConfig`, `IWorkspaceOrchestrator`, etc.).
- Keep scope strictly to **file-based Phase 1** — do **not** add any embedding/vector code.
- After each logical sub-phase (models + loader, CLI surface, generation pipeline, offline + incremental, tests + docs), run `dotnet build && dotnet test`.
- The implementer must update `HANDOFF.md` to reflect the real state after the commit.
- Commit only the step docs + code changes (no .zip files).

## Out of Scope for this step (captured elsewhere)

- Vector / Azure AI Search (GitHub issue #2)
- Publishing / internal feeds (issue #3)
- Full Desktop Workspace UI (acceptable follow-up)
- Automatic org-wide repo discovery

This step delivers immediate high value for cross-repo orientation and sets the perfect foundation for the vector layer that will power semantic “which repos for this story?” queries.
