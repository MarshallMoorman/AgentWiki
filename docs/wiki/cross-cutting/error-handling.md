# Error Handling

> Offline / inventory-derived cross-cutting notes. Verify against source.

## Summary

Error-handling patterns inferred from naming and result types in the inventory.

## Patterns

- Return structured results from long-running operations when possible.
- Fail fast on invalid configuration; fall back gracefully for optional LLM features.

## Key files

- `src/AgentWiki.Core/Models/GenerationResult.cs`
- `src/AgentWiki.Core/Models/RepoAnalysisResult.cs`

## Guidance for agents

- Surface user-friendly CLI errors via Spectre while logging exception details.
- Preserve OperationCanceledException without wrapping.

## Navigation

- [Wiki index](../index.md)
- [Architecture](../architecture.md)
