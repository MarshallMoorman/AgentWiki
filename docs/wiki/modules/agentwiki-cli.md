# AgentWiki.Cli

> Offline / inventory-derived module page. Verify against source.

## Purpose

Project module defined by `src/AgentWiki.Cli/AgentWiki.Cli.csproj`.

## Entry points

- `src/AgentWiki.Cli/Commands/CommandSettingsBase.cs`
- `src/AgentWiki.Cli/Commands/GenerateCommand.cs`
- `src/AgentWiki.Cli/Commands/InitCommand.cs`
- `src/AgentWiki.Cli/Commands/StatusCommand.cs`
- `src/AgentWiki.Cli/Commands/UpdateCommand.cs`
- `src/AgentWiki.Cli/Program.cs`

## Dependencies / roots

- `src/AgentWiki.Cli/`

## Key types / files

- AgentWiki.Cli
- appsettings
- CommandSettingsBase
- GenerateCommand
- InitCommand
- StatusCommand
- UpdateCommand
- TypeRegistrar
- Program
- AgentBootstrapper
- ArchitectureGenerator
- ConfigLoader
- InitService
- MarkdownOutputWriter
- PlaceholderWikiGenerator

## How to extend

- Add new types under `src/AgentWiki.Cli/`.
- Keep public surface area documented in this module page when behavior changes.
- Prefer existing abstractions/interfaces before introducing new layers.

## Gotchas

- This module page was generated offline from file inventory; verify responsibilities against source.
- Related file lists may be capped; inspect the project folder for the full set.

## Related files

- `src/AgentWiki.Cli/AgentWiki.Cli.csproj`
- `src/AgentWiki.Cli/appsettings.json`
- `src/AgentWiki.Cli/Commands/CommandSettingsBase.cs`
- `src/AgentWiki.Cli/Commands/GenerateCommand.cs`
- `src/AgentWiki.Cli/Commands/InitCommand.cs`
- `src/AgentWiki.Cli/Commands/StatusCommand.cs`
- `src/AgentWiki.Cli/Commands/UpdateCommand.cs`
- `src/AgentWiki.Cli/Infrastructure/TypeRegistrar.cs`
- `src/AgentWiki.Cli/Program.cs`
- `src/AgentWiki.Cli/Prompts/ArchitectureOverviewPrompt.txt`
- `src/AgentWiki.Cli/Prompts/CrossCuttingPrompt.txt`
- `src/AgentWiki.Cli/Prompts/CrossLinkValidationPrompt.txt`
- `src/AgentWiki.Cli/Prompts/ModuleAnalysisPrompt.txt`
- `src/AgentWiki.Cli/Prompts/ModulePlanPrompt.txt`
- `src/AgentWiki.Cli/Prompts/SystemPrompt.txt`
- `src/AgentWiki.Cli/Services/AgentBootstrapper.cs`
- `src/AgentWiki.Cli/Services/ArchitectureGenerator.cs`
- `src/AgentWiki.Cli/Services/ConfigLoader.cs`
- `src/AgentWiki.Cli/Services/InitService.cs`
- `src/AgentWiki.Cli/Services/MarkdownOutputWriter.cs`
- `src/AgentWiki.Cli/Services/PlaceholderWikiGenerator.cs`
- `src/AgentWiki.Cli/Services/PromptManager.cs`
- `src/AgentWiki.Cli/Services/RepoAnalyzer.cs`
- `src/AgentWiki.Cli/Services/SemanticKernelLlmCompletionService.cs`
- `src/AgentWiki.Cli/Services/SemanticWikiGenerator.cs`

## Navigation

- [Wiki index](../index.md)
- [Architecture](../architecture.md)
