# Testing

> Cross-cutting notes derived from the current file inventory.

## Summary

Test projects and files discovered during analysis.

## Patterns

- Keep unit tests close to behavior; prefer deterministic offline fixtures for LLM paths.
- Use temp directories for filesystem-facing tests and clean up afterward.

## Key files

- `tests/AgentWiki.Cli.Tests/AgentWiki.Cli.Tests.csproj`
- `tests/AgentWiki.Cli.Tests/Analysis/FileCategorizerTests.cs`
- `tests/AgentWiki.Cli.Tests/Analysis/GitIgnoreMatcherTests.cs`
- `tests/AgentWiki.Cli.Tests/Analysis/PromptTextTests.cs`
- `tests/AgentWiki.Cli.Tests/Generation/ArchitectureMarkdownRendererTests.cs`
- `tests/AgentWiki.Cli.Tests/Generation/CostEstimatorTests.cs`
- `tests/AgentWiki.Cli.Tests/Generation/LlmJsonTests.cs`
- `tests/AgentWiki.Cli.Tests/Integration/EndToEndOfflineTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/AgentBootstrapperTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/ArchitectureGeneratorTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/ConfigLoaderTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/DotEnvLoaderTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/GitChangeDetectorTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/InitServiceTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/LastRunStoreTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/PlaceholderWikiGeneratorTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/PromptManagerTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/RepoAnalyzerTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/SemanticKernelSettingsTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/SemanticWikiGeneratorTests.cs`

## Guidance for agents

- Add tests for new orchestrator steps and bootstrap edge cases.
- Mock ILlmCompletionService rather than calling live models in CI.

## Navigation

- [Wiki index](../index.md)
- [Architecture](../architecture.md)
