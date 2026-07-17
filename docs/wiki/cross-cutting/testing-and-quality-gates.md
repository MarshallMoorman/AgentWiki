# Testing and Quality Gates

> Current cross-cutting documentation (AI-assisted).

## Summary

The repository includes a substantial automated test suite and CI workflows that act as the primary quality gate for changes.

## Patterns

- Dedicated test projects
- CI-based validation
- Workflow automation
- Regression-focused verification

## Key files

- `.github/workflows/ci.yml`
- `CONTRIBUTING.md`
- `AGENTS.md`

## Guidance for agents

- Add or update tests whenever behavior changes.
- Keep new features compatible with automated CI execution.
- Use existing testing patterns rather than introducing isolated validation mechanisms.
- Verify generated-output changes with focused tests where practical.

## Navigation

- [Wiki index](../index.md)
- [Architecture](../architecture.md)
