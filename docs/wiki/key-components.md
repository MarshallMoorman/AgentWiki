# Key Components

> Combines structured architecture components with live inventory.

## From architecture generation

- **CommandSettingsBase.cs** (`src/AgentWiki.Cli/Commands/CommandSettingsBase.cs`): Source file (C#)
- **GenerateCommand.cs** (`src/AgentWiki.Cli/Commands/GenerateCommand.cs`): Source file (C#)
- **InitCommand.cs** (`src/AgentWiki.Cli/Commands/InitCommand.cs`): Source file (C#)
- **StatusCommand.cs** (`src/AgentWiki.Cli/Commands/StatusCommand.cs`): Source file (C#)
- **UpdateCommand.cs** (`src/AgentWiki.Cli/Commands/UpdateCommand.cs`): Source file (C#)
- **TypeRegistrar.cs** (`src/AgentWiki.Cli/Infrastructure/TypeRegistrar.cs`): Source file (C#)
- **Program.cs** (`src/AgentWiki.Cli/Program.cs`): Source file (C#)
- **ArchitectureGenerator.cs** (`src/AgentWiki.Cli/Services/ArchitectureGenerator.cs`): Source file (C#)
- **ConfigLoader.cs** (`src/AgentWiki.Cli/Services/ConfigLoader.cs`): Source file (C#)
- **InitService.cs** (`src/AgentWiki.Cli/Services/InitService.cs`): Source file (C#)
- **MarkdownOutputWriter.cs** (`src/AgentWiki.Cli/Services/MarkdownOutputWriter.cs`): Source file (C#)
- **PlaceholderWikiGenerator.cs** (`src/AgentWiki.Cli/Services/PlaceholderWikiGenerator.cs`): Source file (C#)
- **PromptManager.cs** (`src/AgentWiki.Cli/Services/PromptManager.cs`): Source file (C#)
- **RepoAnalyzer.cs** (`src/AgentWiki.Cli/Services/RepoAnalyzer.cs`): Source file (C#)
- **SemanticKernelLlmCompletionService.cs** (`src/AgentWiki.Cli/Services/SemanticKernelLlmCompletionService.cs`): Source file (C#)

## Languages (inventory)

| Language | Files |
|----------|------:|
| C# | 47 |
| Markdown | 2 |
| JSON | 1 |

## Selected source files

- `src/AgentWiki.Cli/Commands/CommandSettingsBase.cs` (~52 lines)
- `src/AgentWiki.Cli/Commands/GenerateCommand.cs` (~138 lines)
- `src/AgentWiki.Cli/Commands/InitCommand.cs` (~61 lines)
- `src/AgentWiki.Cli/Commands/StatusCommand.cs` (~201 lines)
- `src/AgentWiki.Cli/Commands/UpdateCommand.cs` (~61 lines)
- `src/AgentWiki.Cli/Infrastructure/TypeRegistrar.cs` (~44 lines)
- `src/AgentWiki.Cli/Program.cs` (~93 lines)
- `src/AgentWiki.Cli/Services/ArchitectureGenerator.cs` (~151 lines)
- `src/AgentWiki.Cli/Services/ConfigLoader.cs` (~201 lines)
- `src/AgentWiki.Cli/Services/InitService.cs` (~195 lines)
- `src/AgentWiki.Cli/Services/MarkdownOutputWriter.cs` (~72 lines)
- `src/AgentWiki.Cli/Services/PlaceholderWikiGenerator.cs` (~380 lines)
- `src/AgentWiki.Cli/Services/PromptManager.cs` (~104 lines)
- `src/AgentWiki.Cli/Services/RepoAnalyzer.cs` (~410 lines)
- `src/AgentWiki.Cli/Services/SemanticKernelLlmCompletionService.cs` (~221 lines)
- `src/AgentWiki.Cli/Services/SemanticWikiGenerator.cs` (~298 lines)
- `src/AgentWiki.Core/Abstractions/IArchitectureGenerator.cs` (~19 lines)
- `src/AgentWiki.Core/Abstractions/IConfigLoader.cs` (~32 lines)
- `src/AgentWiki.Core/Abstractions/IInitService.cs` (~34 lines)
- `src/AgentWiki.Core/Abstractions/ILlmCompletionService.cs` (~34 lines)
- `src/AgentWiki.Core/Abstractions/IOutputWriter.cs` (~19 lines)
- `src/AgentWiki.Core/Abstractions/IPromptManager.cs` (~18 lines)
- `src/AgentWiki.Core/Abstractions/IRepoAnalyzer.cs` (~17 lines)
- `src/AgentWiki.Core/Abstractions/IWikiGenerator.cs` (~17 lines)
- `src/AgentWiki.Core/Analysis/FileCategorizer.cs` (~184 lines)
- `src/AgentWiki.Core/Analysis/GitIgnoreMatcher.cs` (~299 lines)
- `src/AgentWiki.Core/Analysis/RepoSummaryBuilder.cs` (~95 lines)
- `src/AgentWiki.Core/Constants/AgentWikiConstants.cs` (~28 lines)
- `src/AgentWiki.Core/Generation/ArchitectureMarkdownRenderer.cs` (~149 lines)
- `src/AgentWiki.Core/Generation/OfflineArchitectureGenerator.cs` (~140 lines)
- `src/AgentWiki.Core/Models/AgentWikiConfig.cs` (~78 lines)
- `src/AgentWiki.Core/Models/ArchitectureDocument.cs` (~82 lines)
- `src/AgentWiki.Core/Models/GenerationResult.cs` (~70 lines)
- `src/AgentWiki.Core/Models/RepoAnalysisResult.cs` (~43 lines)
- `src/AgentWiki.Core/Models/RepoFile.cs` (~35 lines)
- `src/AgentWiki.Core/Models/WikiGenerationRequest.cs` (~34 lines)
- `src/AgentWiki.Core/Models/WikiSection.cs` (~11 lines)
