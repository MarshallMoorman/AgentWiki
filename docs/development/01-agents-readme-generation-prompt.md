You are implementing **Step 01** of the AgentWiki project (current version 1.2.x → target 1.3.0).

**Requirements file (read it fully before coding):**  
`docs/development/01-agents-readme-generation-requirements.md`

(If the file is not yet in that location, the user will place it there from the package you were given.)

## What to build

1. A new CLI command that generates a **complete** `AGENTS.md` (full file, not a snippet) from repo analysis, existing wiki, and discovered instruction files.
2. During `generate` (and appropriately on `update`):
   - If no usable `AGENTS.md` exists → create the full file.
   - If no `README.md` exists **or** it is detected as a generic/template sample → create a good informative README.md.
3. If `.github/copilot-instructions.md` (or equivalent) exists, incorporate its content into the new/updated AGENTS.md and **then delete** the old file (only after successful write; respect dry-run).
4. **Self-updating instructions (required):** Every generated AGENTS.md must include a clear section that tells agents to update AGENTS.md and README.md whenever they make changes that affect how agents should work on the repo (new modules, changed commands, new conventions, workflow changes, etc.). This section must be present in both the offline template and any LLM-enriched output.

Keep the existing small-block `AgentBootstrapper` behavior for repositories that already have a substantial AGENTS.md.

## Architecture constraints (do not violate)

- Follow existing layering: Core (models, helpers, analysis) → App (services) → thin Cli host.
- Register new services via `AddAgentWikiServices()`.
- Offline path must work without LLM credentials.
- Reuse `RepoAnalyzer`, static analysis / Roslyn results, config layering, logging, dry-run patterns.
- Prefer new focused services such as `IAgentsMdGenerator` / `AgentsMdGenerator` and README generation helper so both the new command and `generate` can call them.
- Do not break existing tests for the bootstrap block.

## Implementation guidance

- Start by reading the current `AgentBootstrapper`, `GenerateCommand`, config models, and how wiki generation finishes.
- Implement the full AGENTS.md generator first (offline-quality template + optional LLM enrichment).
- Add generic-README detection heuristics and a solid offline README generator.
- Wire the new behavior into `generate` behind the new config flags (default true).
- Add the new CLI command and help text.
- Comprehensive tests (especially offline + dry-run + migration/deletion safety).
- Be conservative: never overwrite a real README or a rich AGENTS.md unless explicitly forced.

## After implementation (mandatory)

1. Run `dotnet build` and `dotnet test` — everything must be green.
2. Bump version in `Directory.Build.props` **and** `AgentWikiConstants.Version` (use the bump-version skill/script if available; otherwise keep them in sync manually). Prefer a minor bump to 1.3.0 if this is the first 1.3 work, or the next appropriate version.
3. Update user-facing docs (`README.md`) with the new `agents` command and the auto-generation behavior of `generate`.
4. **Commit the requirements.md and prompt.md files** for this step (they belong under `docs/development/`).
5. Do **not** commit any `.zip` files.
6. **Update `docs/HANDOFF.md`** so it accurately reflects the real repository state after this commit (what was built, current version, recommended next step). Update `AGENTS.md` of this repo only if conventions changed.
7. Create one clean commit only if everything is green.

## Acceptance criteria (must all pass)

- `agent-wiki agents` produces a complete, useful AGENTS.md.
- Every generated AGENTS.md contains a clear self-updating section instructing agents to keep AGENTS.md and README.md current.
- `generate` creates a full AGENTS.md when missing (including the self-updating section).
- `generate` creates a good README when missing or generic; leaves real READMEs alone.
- Copilot-instructions content is migrated and the source file is removed after success.
- Dry-run never writes or deletes.
- Existing bootstrap-block behavior still works for rich AGENTS.md files.
- All tests green; new tests cover the new paths (including presence of the self-updating section).
- HANDOFF.md, README.md, version, and step docs are updated and committed.

Confirm you have read the requirements file, then implement Step 01. Prefer small, reviewable changes. Ask for clarification only if a requirement is truly ambiguous.
