# Step 01 — Full AGENTS.md + README Generation

**Project:** AgentWiki  
**Version target:** 1.3.0 (or next minor after current 1.2.x)  
**Date:** 2026-07-17  
**Status:** Ready for implementation  

## Context

v1.2 single-repo polish is complete. Current `AgentBootstrapper` only injects a small marked block (`<!-- BEGIN AGENTWIKI -->` … `<!-- END AGENTWIKI -->`) into an existing or newly-created `AGENTS.md` / `CLAUDE.md`.

This step upgrades agent instruction handling and adds intelligent README generation so that running `agent-wiki generate` on a sparse or greenfield repo produces high-quality, agent-ready documentation files immediately.

## Goals

1. **New command** `agent-wiki agents` (or `agent-wiki generate-agents`) that produces a **complete, high-quality `AGENTS.md`** from the current repository analysis, existing wiki (if present), patterns, and any discovered instruction files. This is a full file, not a snippet.

2. During **`agent-wiki generate`** (and `update` when appropriate):
   - If no `AGENTS.md` (or configured agent path) exists → create the **full** AGENTS.md using the same generator as the new command.
   - Keep the existing small bootstrap block behavior only when a substantial AGENTS.md already exists (preserve user content + refresh the AgentWiki section).

3. During **`agent-wiki generate`**:
   - If no `README.md` exists, **or** the existing README is detected as a generic / empty / template sample → generate a solid, informative `README.md` for the repository.
   - Do **not** overwrite a real, content-rich README.

4. **Copilot instructions migration**:
   - If `.github/copilot-instructions.md` (or other well-known locations) exists, incorporate its content into the new/updated `AGENTS.md`.
   - After successful incorporation and write of AGENTS.md, **delete** the old `copilot-instructions.md` file (with clear logging and dry-run support).
   - Do not delete if the write failed or dry-run is active.

## Non-Goals

- Changing the core wiki generation pipeline (architecture / modules / endpoints / etc.).
- Multi-repo / workspace features.
- Overwriting user-authored, non-generic README.md files.
- Generating CLAUDE.md / GEMINI.md as primary (continue preferring AGENTS.md; fall back to existing CLAUDE.md only when already present and AGENTS.md is missing, consistent with current bootstrapper).
- Automatic commit of the generated files (user / CI decides).

## Functional Requirements

### A. New `agents` command

```bash
agent-wiki agents [options]
```

Options should mirror common generate options where sensible:
- `--repo-path` / `-r`
- `--output` (path for AGENTS.md, default `AGENTS.md`)
- `--force` (overwrite even if a substantial file exists)
- `--dry-run`
- `--verbose`
- Model / provider overrides if LLM enrichment is used

Behavior:
- Analyze the repo (reuse `RepoAnalyzer` + optional Roslyn / existing analysis).
- If a wiki already exists under the configured output path, incorporate key facts from `index.md`, `architecture.md`, etc.
- Discover and load any existing instruction sources (see section D).
- Produce a complete, well-structured `AGENTS.md`.
- Support dry-run (show what would be written).

### B. Integration into `generate`

After wiki generation succeeds (or as a late step):

1. **AGENTS.md**
   - If the target agent file does **not** exist → run the full AGENTS.md generator and write the complete file.
   - If it exists and already contains the AgentWiki marker block → keep current “update block only” behavior (do not replace the whole file).
   - If it exists but is empty / trivial → treat as missing and write the full file.
   - Config flag (default true): `generateAgentsMdIfMissing`.

2. **README.md**
   - Detect “generic” README (see heuristics below).
   - If missing or generic → generate a good README.
   - Config flag (default true): `generateReadmeIfMissingOrGeneric`.
   - Never overwrite a README that fails the “generic” test unless `--force` is explicitly passed for README (or a dedicated flag).

### C. Full AGENTS.md Content Expectations

The generated file should be a complete, useful document for any coding agent (Copilot, Claude, Cursor, Codex, etc.). Suggested structure (adapt intelligently to the repo):

```markdown
# AGENTS.md — <Repo Name>

## Start here
- Ordered list of the most important files to read first (HANDOFF, README, architecture, etc. when they exist).

## Project snapshot
- One-paragraph purpose
- Primary language(s) / framework(s)
- Key commands (build, test, run, pack)
- Important config / env notes

## Architecture & layout
- High-level structure (link into docs/wiki/ when present)
- Major modules / projects

## Coding conventions
- Detected or inferred conventions (nullable, primary constructors, test framework, etc.)
- Any rules pulled from existing instruction files

## AgentWiki
- The standard AgentWiki block (so agents know about the wiki)

## Keep this file (and README) up to date
- When you make changes that affect how agents should work on this repo, update AGENTS.md in the same change.
- Examples of changes that require an AGENTS.md update:
  - New or removed major modules / projects / entry points
  - Changed build, test, run, or pack commands
  - New coding conventions, guardrails, or “do not” rules
  - New required tools, env vars, or secrets handling
  - Changes to the preferred workflow for agents
- When you change user-facing setup, quick start, or primary commands, update README.md in the same change.
- Prefer small, precise edits to the existing sections rather than rewriting the whole file.
- If AgentWiki is in use, re-run `agent-wiki generate` / `update` (or `agent-wiki agents`) when structural changes make the wiki or AGENTS.md stale.

## Do not / Guardrails
- Common “do not” items (secrets, force-push, etc.) plus any project-specific ones

## Common tasks
- Table or list of frequent agent tasks → where to look / what to run
```

**Self-updating requirement (mandatory):**  
Every generated AGENTS.md **must** include a clear, actionable section (similar to the one above) that instructs agents to keep both `AGENTS.md` and `README.md` current when they make relevant changes. This is not optional boilerplate — it is a core part of the value of the generated file.

Use LLM enrichment when credentials are available; fall back to a high-quality offline template driven by inventory + static analysis + existing docs. The self-updating section must appear in both the offline template and any LLM-enriched output.

### D. Instruction File Discovery & Migration

Search order (first hits win / merge):

1. `.github/copilot-instructions.md` (primary GitHub Copilot location)
2. `copilot-instructions.md` at repo root (less common)
3. Optionally other well-known files if easy: `.github/instructions/*.instructions.md` (path-specific — lower priority, consider summarizing or linking)

Behavior:
- Read content.
- Incorporate into the AGENTS.md under a clear section (e.g. “Project-specific instructions (migrated from copilot-instructions)” or intelligently merged into Conventions / Guardrails).
- After AGENTS.md is successfully written, delete the source `copilot-instructions.md`.
- Log clearly: “Migrated content from .github/copilot-instructions.md and removed the file.”
- Dry-run: show that the file would be deleted; do not delete.
- If multiple instruction files exist, prefer the primary one and log the others.

### E. README Generation Heuristics

Treat as “generic / needs replacement” when **any** of the following are true:

- File does not exist.
- Length < ~400–600 characters (configurable).
- Contains common template markers: “TODO”, “Your Project”, “Replace this”, “dotnet new”, “Visual Studio”, “ASP.NET Core Web API” default text, “Getting Started” with no real project-specific content, etc.
- Very few headings and almost no project-specific names/commands.

Generated README should include:
- Project name + short description (from analysis / existing docs)
- Build / test / run instructions
- Key configuration
- Link to `docs/wiki/` when present
- Link to AGENTS.md
- License if detectable

Prefer offline generation; optional LLM polish when available.

### F. Configuration

Add to `AgentWikiConfig` (with sensible defaults):

- `generateAgentsMdIfMissing` (bool, default `true`)
- `generateReadmeIfMissingOrGeneric` (bool, default `true`)
- `agentsMdPath` (already partially exists via agent path)
- `readmeGenericMaxLength` / marker list (optional, keep simple)
- `migrateCopilotInstructions` (bool, default `true`)

Respect existing config priority (CLI > .env > config.json > …).

### G. CLI / Desktop / UX

- New command appears in help and Desktop (if easy parity; Desktop can come in a follow-up if scope is tight).
- Clear console messages: “Created full AGENTS.md”, “Migrated and removed .github/copilot-instructions.md”, “Generated README.md (previous was generic template)”, etc.
- Dry-run support for all write/delete actions.
- `--force` on the new `agents` command overwrites a full existing AGENTS.md.

### H. Testing

- Unit tests for:
  - Full AGENTS.md generator (offline path)
  - Generic README detection heuristics
  - Copilot-instructions discovery + migration (including dry-run does not delete)
  - Integration into generate when files are missing vs present
- Offline end-to-end test that a sparse fixture repo ends up with both AGENTS.md and README.md.
- Existing bootstrap-block tests must continue to pass when a rich AGENTS.md already exists.

### I. Documentation Updates (mandatory)

After implementation:
- Update `README.md` (user-facing) with the new `agents` command and the auto-generation behavior of `generate`.
- Update `docs/HANDOFF.md` with what changed, current version, and recommended next step.
- Update `AGENTS.md` of **this** repo only if conventions changed.
- Update `CONTRIBUTING.md` if needed.
- Commit the requirements + prompt files for this step under `docs/development/`.

## Acceptance Criteria

- [ ] `agent-wiki agents` produces a complete, useful AGENTS.md on a real repo (offline and with LLM).
- [ ] Every generated AGENTS.md contains a clear **self-updating** section that instructs agents to keep AGENTS.md and README.md current when they make relevant changes.
- [ ] `agent-wiki generate` on a repo with no AGENTS.md creates the full file (including the self-updating section).
- [ ] `agent-wiki generate` on a repo with no README or a generic README creates a good README.
- [ ] Existing rich AGENTS.md / README.md are **not** overwritten (unless forced).
- [ ] `.github/copilot-instructions.md` content is incorporated and the file is removed after successful write.
- [ ] Dry-run never writes or deletes.
- [ ] All existing tests pass; new tests cover the new behavior (including presence of the self-updating section).
- [ ] Version bumped, HANDOFF.md updated to reflect real state, requirements/prompt committed.
- [ ] No secrets logged; offline path remains fully functional.

## Implementation Notes

- Reuse `RepoAnalyzer`, `IStaticAnalyzer` / Roslyn results, existing prompt infrastructure, and `WikiPostProcessor` patterns where helpful.
- Prefer extracting a dedicated `IAgentsMdGenerator` / `AgentsMdGenerator` and `IReadmeGenerator` (or similar) in App/Core so both the new command and `generate` can call them.
- Keep the current small-block `AgentBootstrapper` for the “already has substantial AGENTS.md” case; do not break it.
- Be conservative on file deletion — only delete copilot-instructions after confirmed successful AGENTS.md write.

## Out of Scope for this step

- Desktop UI for the new command (acceptable to add basic parity if low-effort; otherwise follow-up).
- Multi-repo awareness.
- Generating additional agent files (CLAUDE.md, etc.) as primary.
