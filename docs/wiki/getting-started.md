# Getting Started for Coding Agents

This repository maintains an **agent-optimized wiki** at `docs/wiki/`.

## Recommended workflow

1. Open `index.md` for navigation and quick facts.
2. Read `architecture.md` before changing structure or dependencies.
3. Use `key-components.md` and `inventory.md` for real file paths discovered in the repo.
4. Prefer updating code over inventing undocumented patterns.

## Important

- Inventory data is derived from the live tree (respecting `.gitignore` + config ignores).
- Narrative content may still be placeholder / AI-generated; verify against source of truth.
- Do not commit secrets into wiki pages.
- After meaningful structural changes, re-run `agent-wiki generate` or `update`.
