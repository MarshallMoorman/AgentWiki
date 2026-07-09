# Repository Inventory

> Machine-generated from RepoAnalyzer.

```text
# Repository: agent-wiki
Path: /Users/mmoorman/dev/ea/agent-wiki

## Inventory summary
- Total files (after ignores): 59
- Selected for analysis: 59
- Total size: 206.5 KB
- Approximate lines (text files): 5,874

## Files by category
- SourceCode: 37
- Documentation: 4
- Configuration: 7
- Tests: 11
- Diagrams: 0
- Other: 0

## Detected languages
- C#: 47 file(s)
- Markdown: 2 file(s)
- JSON: 1 file(s)

## Top folders
- `src/` — 42 files (151.0 KB)
- `tests/` — 11 files (30.9 KB)
- `(root)/` — 6 files (24.6 KB)

## Top extensions
- .cs: 47
- .csproj: 3
- .md: 2
- .txt: 2
- .editorconfig: 1
- .gitignore: 1
- .slnx: 1
- .props: 1
- .json: 1

## Selected files (sample for analysis)
- `.editorconfig` [Configuration, ~28 lines]
- `.gitignore` [Configuration, ~48 lines]
- `AgentWiki-Project-Specification.md` [Documentation, ~476 lines]
- `AgentWiki.slnx` [Configuration, ~9 lines]
- `Directory.Build.props` [Configuration, ~21 lines]
- `README.md` [Documentation, ~102 lines]
- `src/AgentWiki.Cli/AgentWiki.Cli.csproj` [Configuration, ~49 lines]
- `src/AgentWiki.Cli/appsettings.json` [Configuration, ~30 lines]
- `src/AgentWiki.Cli/Commands/CommandSettingsBase.cs` [SourceCode, ~52 lines]
- `src/AgentWiki.Cli/Commands/GenerateCommand.cs` [SourceCode, ~138 lines]
- `src/AgentWiki.Cli/Commands/InitCommand.cs` [SourceCode, ~61 lines]
- `src/AgentWiki.Cli/Commands/StatusCommand.cs` [SourceCode, ~201 lines]
- `src/AgentWiki.Cli/Commands/UpdateCommand.cs` [SourceCode, ~61 lines]
- `src/AgentWiki.Cli/Infrastructure/TypeRegistrar.cs` [SourceCode, ~44 lines]
- `src/AgentWiki.Cli/Program.cs` [SourceCode, ~93 lines]
- `src/AgentWiki.Cli/Prompts/ArchitectureOverviewPrompt.txt` [Documentation, ~30 lines]
- `src/AgentWiki.Cli/Prompts/SystemPrompt.txt` [Documentation, ~12 lines]
- `src/AgentWiki.Cli/Services/ArchitectureGenerator.cs` [SourceCode, ~151 lines]
- `src/AgentWiki.Cli/Services/ConfigLoader.cs` [SourceCode, ~201 lines]
- `src/AgentWiki.Cli/Services/InitService.cs` [SourceCode, ~195 lines]
- `src/AgentWiki.Cli/Services/MarkdownOutputWriter.cs` [SourceCode, ~72 lines]
- `src/AgentWiki.Cli/Services/PlaceholderWikiGenerator.cs` [SourceCode, ~380 lines]
- `src/AgentWiki.Cli/Services/PromptManager.cs` [SourceCode, ~104 lines]
- `src/AgentWiki.Cli/Services/RepoAnalyzer.cs` [SourceCode, ~410 lines]
- `src/AgentWiki.Cli/Services/SemanticKernelLlmCompletionService.cs` [SourceCode, ~221 lines]
- `src/AgentWiki.Cli/Services/SemanticWikiGenerator.cs` [SourceCode, ~298 lines]
- `src/AgentWiki.Core/Abstractions/IArchitectureGenerator.cs` [SourceCode, ~19 lines]
- `src/AgentWiki.Core/Abstractions/IConfigLoader.cs` [SourceCode, ~32 lines]
- `src/AgentWiki.Core/Abstractions/IInitService.cs` [SourceCode, ~34 lines]
- `src/AgentWiki.Core/Abstractions/ILlmCompletionService.cs` [SourceCode, ~34 lines]
- `src/AgentWiki.Core/Abstractions/IOutputWriter.cs` [SourceCode, ~19 lines]
- `src/AgentWiki.Core/Abstractions/IPromptManager.cs` [SourceCode, ~18 lines]
- `src/AgentWiki.Core/Abstractions/IRepoAnalyzer.cs` [SourceCode, ~17 lines]
- `src/AgentWiki.Core/Abstractions/IWikiGenerator.cs` [SourceCode, ~17 lines]
- `src/AgentWiki.Core/AgentWiki.Core.csproj` [Configuration, ~10 lines]
- `src/AgentWiki.Core/Analysis/FileCategorizer.cs` [SourceCode, ~184 lines]
- `src/AgentWiki.Core/Analysis/GitIgnoreMatcher.cs` [SourceCode, ~299 lines]
- `src/AgentWiki.Core/Analysis/RepoSummaryBuilder.cs` [SourceCode, ~95 lines]
- `src/AgentWiki.Core/Constants/AgentWikiConstants.cs` [SourceCode, ~28 lines]
- `src/AgentWiki.Core/Generation/ArchitectureMarkdownRenderer.cs` [SourceCode, ~149 lines]
- `src/AgentWiki.Core/Generation/OfflineArchitectureGenerator.cs` [SourceCode, ~140 lines]
- `src/AgentWiki.Core/Models/AgentWikiConfig.cs` [SourceCode, ~78 lines]
- `src/AgentWiki.Core/Models/ArchitectureDocument.cs` [SourceCode, ~82 lines]
- `src/AgentWiki.Core/Models/GenerationResult.cs` [SourceCode, ~70 lines]
- `src/AgentWiki.Core/Models/RepoAnalysisResult.cs` [SourceCode, ~43 lines]
- `src/AgentWiki.Core/Models/RepoFile.cs` [SourceCode, ~35 lines]
- `src/AgentWiki.Core/Models/WikiGenerationRequest.cs` [SourceCode, ~34 lines]
- `src/AgentWiki.Core/Models/WikiSection.cs` [SourceCode, ~11 lines]
- `tests/AgentWiki.Cli.Tests/AgentWiki.Cli.Tests.csproj` [Tests, ~37 lines]
- `tests/AgentWiki.Cli.Tests/Analysis/FileCategorizerTests.cs` [Tests, ~42 lines]
- `tests/AgentWiki.Cli.Tests/Analysis/GitIgnoreMatcherTests.cs` [Tests, ~107 lines]
- `tests/AgentWiki.Cli.Tests/Generation/ArchitectureMarkdownRendererTests.cs` [Tests, ~45 lines]
- `tests/AgentWiki.Cli.Tests/Services/ArchitectureGeneratorTests.cs` [Tests, ~192 lines]
- `tests/AgentWiki.Cli.Tests/Services/ConfigLoaderTests.cs` [Tests, ~106 lines]
- `tests/AgentWiki.Cli.Tests/Services/InitServiceTests.cs` [Tests, ~86 lines]
- `tests/AgentWiki.Cli.Tests/Services/PlaceholderWikiGeneratorTests.cs` [Tests, ~68 lines]
- `tests/AgentWiki.Cli.Tests/Services/PromptManagerTests.cs` [Tests, ~36 lines]
- `tests/AgentWiki.Cli.Tests/Services/RepoAnalyzerTests.cs` [Tests, ~122 lines]
- `tests/AgentWiki.Cli.Tests/Services/SemanticWikiGeneratorTests.cs` [Tests, ~98 lines]
```
