# AgentWiki.Core

> Offline / inventory-derived module page. Verify against source.

## Purpose

Project module defined by `src/AgentWiki.Core/AgentWiki.Core.csproj`.

## Entry points

_None listed._

## Dependencies / roots

- `src/AgentWiki.Core/`

## Key types / files

- IAgentBootstrapper
- IArchitectureGenerator
- IConfigLoader
- IInitService
- ILlmCompletionService
- IOutputWriter
- IPromptManager
- IRepoAnalyzer
- IWikiGenerationOrchestrator
- IWikiGenerator
- AgentWiki.Core
- FileCategorizer
- GitIgnoreMatcher
- RepoSummaryBuilder
- AgentWikiConstants

## How to extend

- Add new types under `src/AgentWiki.Core/`.
- Keep public surface area documented in this module page when behavior changes.
- Prefer existing abstractions/interfaces before introducing new layers.

## Gotchas

- This module page was generated offline from file inventory; verify responsibilities against source.
- Related file lists may be capped; inspect the project folder for the full set.

## Related files

- `src/AgentWiki.Core/Abstractions/IAgentBootstrapper.cs`
- `src/AgentWiki.Core/Abstractions/IArchitectureGenerator.cs`
- `src/AgentWiki.Core/Abstractions/IConfigLoader.cs`
- `src/AgentWiki.Core/Abstractions/IInitService.cs`
- `src/AgentWiki.Core/Abstractions/ILlmCompletionService.cs`
- `src/AgentWiki.Core/Abstractions/IOutputWriter.cs`
- `src/AgentWiki.Core/Abstractions/IPromptManager.cs`
- `src/AgentWiki.Core/Abstractions/IRepoAnalyzer.cs`
- `src/AgentWiki.Core/Abstractions/IWikiGenerationOrchestrator.cs`
- `src/AgentWiki.Core/Abstractions/IWikiGenerator.cs`
- `src/AgentWiki.Core/AgentWiki.Core.csproj`
- `src/AgentWiki.Core/Analysis/FileCategorizer.cs`
- `src/AgentWiki.Core/Analysis/GitIgnoreMatcher.cs`
- `src/AgentWiki.Core/Analysis/RepoSummaryBuilder.cs`
- `src/AgentWiki.Core/Constants/AgentWikiConstants.cs`
- `src/AgentWiki.Core/Generation/ArchitectureMarkdownRenderer.cs`
- `src/AgentWiki.Core/Generation/ModuleMarkdownRenderer.cs`
- `src/AgentWiki.Core/Generation/OfflineArchitectureGenerator.cs`
- `src/AgentWiki.Core/Generation/OfflineModulePlanner.cs`
- `src/AgentWiki.Core/Generation/TokenUsageMath.cs`
- `src/AgentWiki.Core/Models/AgentWikiConfig.cs`
- `src/AgentWiki.Core/Models/ArchitectureDocument.cs`
- `src/AgentWiki.Core/Models/GenerationResult.cs`
- `src/AgentWiki.Core/Models/RepoAnalysisResult.cs`
- `src/AgentWiki.Core/Models/RepoFile.cs`

## Navigation

- [Wiki index](../index.md)
- [Architecture](../architecture.md)
