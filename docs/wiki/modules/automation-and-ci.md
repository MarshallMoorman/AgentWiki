# Automation, Packaging & CI

> Current module documentation for coding agents (AI-assisted).

## Purpose

Provides repository automation for continuous integration, tool packaging/installation, desktop execution, version-bump workflows, and wiki refresh operations through GitHub Actions and supporting scripts.

## Entry points

- `.github/workflows/ci.yml`
- `.github/workflows/wiki-refresh.yml`
- `scripts/pack-and-install-tool.sh`
- `scripts/run-desktop.sh`
- `.grok/skills/bump-version/SKILL.md`
- `.grok/skills/bump-version/scripts/bump-version.sh`

## Dependencies / roots

- `GitHub Actions workflows defined in .github/workflows`
- `Shell script execution environment required by scripts/*.sh`
- `Version-bump skill assets under .grok/skills/bump-version`

## Key types / files

- .github/workflows/ci.yml
- .github/workflows/wiki-refresh.yml
- scripts/pack-and-install-tool.sh
- scripts/run-desktop.sh
- .grok/skills/bump-version/SKILL.md
- .grok/skills/bump-version/scripts/bump-version.sh

## Endpoints / Public API

_No HTTP or Function endpoints discovered for this module._

## How to extend

- Modify .github/workflows/ci.yml when adding new validation, build, packaging, or test automation so repository checks remain consistent.
- Update .github/workflows/wiki-refresh.yml when changing wiki refresh behavior or automation triggers.
- Extend scripts/pack-and-install-tool.sh for packaging or installation process changes instead of duplicating packaging logic elsewhere.
- Use scripts/run-desktop.sh as the integration point for desktop-launch automation changes.
- Keep version-bump workflow documentation in .grok/skills/bump-version/SKILL.md aligned with behavior implemented in .grok/skills/bump-version/scripts/bump-version.sh.
- When introducing new automation, connect it through existing workflow or script entry points and verify any workflow references remain valid.

## Gotchas

- Workflow files in .github/workflows are automation entry points; changes can affect repository-wide validation or operational tasks.
- Shell scripts and workflow definitions are coupled through file paths and invocation conventions; rename or move files only after updating all references.
- Maintain consistency between .grok/skills/bump-version/SKILL.md and .grok/skills/bump-version/scripts/bump-version.sh to avoid documentation and behavior drift.
- Packaging, installation, and execution behavior are script-driven; update the relevant script rather than assuming workflow-only changes are sufficient.

## Related files

- `.github/workflows/ci.yml`
- `.github/workflows/wiki-refresh.yml`
- `scripts/pack-and-install-tool.sh`
- `scripts/run-desktop.sh`
- `.grok/skills/bump-version/SKILL.md`
- `.grok/skills/bump-version/scripts/bump-version.sh`

## Navigation

- [Wiki index](../index.md)
- [Architecture](../architecture.md)
- [API Endpoints](../api-endpoints.md)
