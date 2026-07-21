You are implementing **Step 02** of the AgentWiki project (current version 1.3.x → target 1.4.0).

**Requirements file (read it fully before coding):**  
`docs/development/02-multi-repo-workspace-phase1-requirements.md`

(If the file is not yet in that location, the user will place it there from the package you were given.)

## What to build

Implement **Phase 1 only**: file-based multi-repo Workspace support for AgentWiki.

**Strict scope reminder:**
- File-based / Markdown only. **Do NOT** implement any vector embeddings, Azure AI Search, RAG, or semantic retrieval in this step.
- Reuse existing single-repo code as much as possible (`RepoAnalyzer`, orchestrator, offline builders, `AgentsMdOfflineBuilder`, incremental stores, config layering, etc.).
- Keep all existing single-repo commands (`generate`, `update`, `agents`, etc.) completely unaffected.
- New surface lives under the `workspace` subcommand.

Key deliverables from the requirements:
- Workspace config model + loader + validation
- New CLI commands: `workspace init`, `generate`, `update`, `status` (and optionally `add`)
- `WorkspaceLoader` + `MemberResolver` (local paths + remote git shallow clone/cache)
- Ensure member repos have fresh wikis (or clear guidance)
- Cross-repo signal collection (references, packages, contracts, ownership, etc.)
- Multi-step system wiki generation (architecture, dependency graph, data flows, ownership, member index with deep links)
- Workspace-level `AGENTS.md` (full or marker block) with “start at root → drill into members” + self-updating section
- Full offline support + incremental updates (extend `LastRunStore` / `GitChangeDetector`)
- Clear error messages, dry-run, progress, logging
- Tests (offline + integration + dry-run)
- Documentation updates (README, HANDOFF, etc.)

## Architecture constraints (do not violate)

- Clean layering: Core (new models + helpers) → App (new services) → thin CLI host.
- Register via `AddAgentWikiServices()`.
- Follow existing conventions (primary constructors, nullable, modern C#, XML docs).
- Offline path must always work.
- No breaking changes to single-repo behavior or public contracts.

## Implementation guidance

1. Start by reading the GitHub issue #1 (or the requirements file) + existing `AgentWikiConfig`, `SemanticWikiGenerator`, `AgentsMdOfflineBuilder`, `RepoAnalyzer`, `LastRunStore`, and CLI command patterns.
2. Design the models (`WorkspaceConfig`, `WorkspaceMember`, `WorkspaceAnalysisResult`, etc.) first.
3. Implement `WorkspaceLoader` + `MemberResolver` (local + remote caching).
4. Extend or create `WorkspaceOrchestrator` (reuse orchestrator patterns heavily).
5. Wire the new `workspace` subcommand in Spectre.
6. Add offline + incremental paths.
7. Comprehensive tests.
8. Documentation + HANDOFF update.

**Propose a short, concrete phased plan** (e.g. models → loader → CLI surface → generation pipeline → offline/incremental → tests/docs) before writing code, and confirm you understand the **file-based only** scope.

## After implementation (mandatory)

1. Run `dotnet build` and `dotnet test` — everything must be green.
2. Bump version in `Directory.Build.props` **and** `AgentWikiConstants.Version` (use the bump-version skill/script). Prefer a minor bump to 1.4.0 or the next appropriate version.
3. Update user-facing docs (`README.md`) with the new `workspace` commands and examples.
4. **Commit the requirements.md and prompt.md files** for this step (they belong under `docs/development/`).
5. Do **not** commit any `.zip` files.
6. **Update `docs/HANDOFF.md`** so it accurately reflects the real repository state after this commit (what was built, current version, recommended next step — e.g. Phase 2 vectors or Desktop parity). Update `AGENTS.md` of this repo only if conventions changed.
7. Create one clean commit only if everything is green.

## Acceptance criteria (must all pass)

- Workspace config with local + remote members works.
- `workspace generate` produces a useful system-level wiki with deep links into member wikis + workspace `AGENTS.md`.
- `workspace update` is incremental and efficient.
- Full offline mode works.
- Single-repo commands are untouched.
- All tests green; dry-run safe.
- HANDOFF, README, version, and step docs updated and committed.
- No secrets logged; offline path fully functional.

Confirm you have read the requirements file, then implement Step 02. Prefer small, reviewable changes. Ask for clarification only if a requirement is truly ambiguous. Follow the product’s existing architecture and the phased style used for previous features.
