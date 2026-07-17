# Automation and Release Workflows

> Current cross-cutting documentation (AI-assisted).

## Summary

Repository maintenance, wiki generation, packaging, and version-related activities are automated through scripts and workflow definitions.

## Patterns

- GitHub Actions workflows
- Repository refresh automation
- Packaging scripts
- Version-management tooling

## Key files

- `.github/workflows/wiki-refresh.yml`
- `.github/workflows/ci.yml`
- `scripts/pack-and-install-tool.sh`
- `.grok/skills/bump-version/scripts/bump-version.sh`
- `examples/github-actions/agent-wiki-update.yml`
- `examples/azure-pipelines/agent-wiki-update.yml`

## Guidance for agents

- Keep workflow changes aligned with documented automation behavior.
- Preserve noninteractive execution paths for CI environments.
- Update example pipelines when automation contracts change.
- Ensure scripts remain portable and compatible with documented usage patterns.

## Navigation

- [Wiki index](../index.md)
- [Architecture](../architecture.md)
