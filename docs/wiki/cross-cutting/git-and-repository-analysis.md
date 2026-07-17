# Git and Repository Analysis

> Current cross-cutting documentation (AI-assisted).

## Summary

Repository inspection, change detection, and Git integration are core capabilities used to scope generation work and understand project state.

## Patterns

- Git process abstraction
- Change detection services
- Repository-aware workflows
- Command execution wrappers

## Key files

- `src/AgentWiki.App/Services/GitChangeDetector.cs`
- `src/AgentWiki.App/Services/GitProcess.cs`

## Guidance for agents

- Route new Git operations through existing abstractions rather than spawning ad hoc processes.
- Handle repositories with varying histories and states gracefully.
- Preserve change-detection behavior when extending repository analysis features.
- Capture and log Git command failures with actionable diagnostics.

## Navigation

- [Wiki index](../index.md)
- [Architecture](../architecture.md)
