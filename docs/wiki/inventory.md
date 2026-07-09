# Repository Inventory

> Machine-generated from RepoAnalyzer.

```text
# Repository: agent-wiki
Path: /Users/mmoorman/dev/ea/agent-wiki

## Inventory summary
- Total files (after ignores): 89
- Selected for analysis: 89
- Total size: 342.6 KB
- Approximate lines (text files): 9,728

## Files by category
- SourceCode: 52
- Documentation: 11
- Configuration: 9
- Tests: 17
- Diagrams: 0
- Other: 0

## Detected languages
- C#: 68 file(s)
- Markdown: 4 file(s)
- JSON: 2 file(s)
- YAML: 1 file(s)

## Top folders
- `src/` — 61 files (250.1 KB)
- `tests/` — 17 files (54.3 KB)
- `(root)/` — 9 files (34.2 KB)
- `.github/` — 1 files (3.4 KB)
- `examples/` — 1 files (640 B)

## Top extensions
- .cs: 68
- .txt: 6
- .md: 4
- .csproj: 3
- .json: 2
- .editorconfig: 1
- .yml: 1
- .gitignore: 1
- .slnx: 1
- .props: 1
- (no extension): 1

## Selected files (sample for analysis)
- `.editorconfig` [Configuration, ~28 lines]
- `.github/workflows/agent-wiki-update.yml` [Configuration, ~101 lines]
- `.gitignore` [Configuration, ~48 lines]
- `AGENTS.md` [Documentation, ~11 lines]
- `AgentWiki-Project-Specification.md` [Documentation, ~476 lines]
- `AgentWiki.slnx` [Configuration, ~9 lines]
- `CONTRIBUTING.md` [Documentation, ~80 lines]
- `Directory.Build.props` [Configuration, ~21 lines]
- `examples/agentwiki.config.json` [Configuration, ~30 lines]
- `LICENSE` [Documentation, ~21 lines]
- `README.md` [Documentation, ~274 lines]
- `src/AgentWiki.Cli/AgentWiki.Cli.csproj` [Configuration, ~53 lines]
- `src/AgentWiki.Cli/appsettings.json` [Configuration, ~30 lines]
- `src/AgentWiki.Cli/Commands/CommandSettingsBase.cs` [SourceCode, ~52 lines]
- `src/AgentWiki.Cli/Commands/GenerateCommand.cs` [SourceCode, ~168 lines]
- `src/AgentWiki.Cli/Commands/InitCommand.cs` [SourceCode, ~61 lines]
- `src/AgentWiki.Cli/Commands/StatusCommand.cs` [SourceCode, ~247 lines]
- `src/AgentWiki.Cli/Commands/UpdateCommand.cs` [SourceCode, ~59 lines]
- `src/AgentWiki.Cli/Infrastructure/TypeRegistrar.cs` [SourceCode, ~44 lines]
- `src/AgentWiki.Cli/Program.cs` [SourceCode, ~140 lines]
- `src/AgentWiki.Cli/Prompts/ArchitectureOverviewPrompt.txt` [Documentation, ~30 lines]
- `src/AgentWiki.Cli/Prompts/CrossCuttingPrompt.txt` [Documentation, ~21 lines]
- `src/AgentWiki.Cli/Prompts/CrossLinkValidationPrompt.txt` [Documentation, ~17 lines]
- `src/AgentWiki.Cli/Prompts/ModuleAnalysisPrompt.txt` [Documentation, ~22 lines]
- `src/AgentWiki.Cli/Prompts/ModulePlanPrompt.txt` [Documentation, ~25 lines]
- `src/AgentWiki.Cli/Prompts/SystemPrompt.txt` [Documentation, ~12 lines]
- `src/AgentWiki.Cli/Services/AgentBootstrapper.cs` [SourceCode, ~150 lines]
- `src/AgentWiki.Cli/Services/ArchitectureGenerator.cs` [SourceCode, ~151 lines]
- `src/AgentWiki.Cli/Services/ConfigLoader.cs` [SourceCode, ~201 lines]
- `src/AgentWiki.Cli/Services/GitChangeDetector.cs` [SourceCode, ~438 lines]
- `src/AgentWiki.Cli/Services/GitProcess.cs` [SourceCode, ~73 lines]
- `src/AgentWiki.Cli/Services/InitService.cs` [SourceCode, ~216 lines]
- `src/AgentWiki.Cli/Services/LastRunStore.cs` [SourceCode, ~59 lines]
- `src/AgentWiki.Cli/Services/LlmResilience.cs` [SourceCode, ~44 lines]
- `src/AgentWiki.Cli/Services/MarkdownOutputWriter.cs` [SourceCode, ~72 lines]
- `src/AgentWiki.Cli/Services/PlaceholderWikiGenerator.cs` [SourceCode, ~380 lines]
- `src/AgentWiki.Cli/Services/PromptManager.cs` [SourceCode, ~104 lines]
- `src/AgentWiki.Cli/Services/RepoAnalyzer.cs` [SourceCode, ~410 lines]
- `src/AgentWiki.Cli/Services/SemanticKernelLlmCompletionService.cs` [SourceCode, ~234 lines]
- `src/AgentWiki.Cli/Services/SemanticWikiGenerator.cs` [SourceCode, ~346 lines]
- `src/AgentWiki.Cli/Services/WikiGenerationOrchestrator.cs` [SourceCode, ~770 lines]
- `src/AgentWiki.Core/Abstractions/IAgentBootstrapper.cs` [SourceCode, ~42 lines]
- `src/AgentWiki.Core/Abstractions/IArchitectureGenerator.cs` [SourceCode, ~19 lines]
- `src/AgentWiki.Core/Abstractions/IChangeDetector.cs` [SourceCode, ~29 lines]
- `src/AgentWiki.Core/Abstractions/IConfigLoader.cs` [SourceCode, ~32 lines]
- `src/AgentWiki.Core/Abstractions/IInitService.cs` [SourceCode, ~34 lines]
- `src/AgentWiki.Core/Abstractions/ILlmCompletionService.cs` [SourceCode, ~34 lines]
- `src/AgentWiki.Core/Abstractions/IOutputWriter.cs` [SourceCode, ~19 lines]
- `src/AgentWiki.Core/Abstractions/IPromptManager.cs` [SourceCode, ~18 lines]
- `src/AgentWiki.Core/Abstractions/IRepoAnalyzer.cs` [SourceCode, ~17 lines]
- `src/AgentWiki.Core/Abstractions/IWikiGenerationOrchestrator.cs` [SourceCode, ~20 lines]
- `src/AgentWiki.Core/Abstractions/IWikiGenerator.cs` [SourceCode, ~17 lines]
- `src/AgentWiki.Core/AgentWiki.Core.csproj` [Configuration, ~10 lines]
- `src/AgentWiki.Core/Analysis/FileCategorizer.cs` [SourceCode, ~184 lines]
- `src/AgentWiki.Core/Analysis/GitIgnoreMatcher.cs` [SourceCode, ~299 lines]
- `src/AgentWiki.Core/Analysis/RepoSummaryBuilder.cs` [SourceCode, ~95 lines]
- `src/AgentWiki.Core/Constants/AgentWikiConstants.cs` [SourceCode, ~28 lines]
- `src/AgentWiki.Core/Generation/ArchitectureMarkdownRenderer.cs` [SourceCode, ~149 lines]
- `src/AgentWiki.Core/Generation/CostEstimator.cs` [SourceCode, ~66 lines]
- `src/AgentWiki.Core/Generation/ModuleMarkdownRenderer.cs` [SourceCode, ~200 lines]
- `src/AgentWiki.Core/Generation/OfflineArchitectureGenerator.cs` [SourceCode, ~140 lines]
- `src/AgentWiki.Core/Generation/OfflineModulePlanner.cs` [SourceCode, ~288 lines]
- `src/AgentWiki.Core/Generation/TokenUsageMath.cs` [SourceCode, ~27 lines]
- `src/AgentWiki.Core/Models/AgentWikiConfig.cs` [SourceCode, ~78 lines]
- `src/AgentWiki.Core/Models/ArchitectureDocument.cs` [SourceCode, ~82 lines]
- `src/AgentWiki.Core/Models/GenerationResult.cs` [SourceCode, ~82 lines]
- `src/AgentWiki.Core/Models/LastRunState.cs` [SourceCode, ~89 lines]
- `src/AgentWiki.Core/Models/RepoAnalysisResult.cs` [SourceCode, ~43 lines]
- `src/AgentWiki.Core/Models/RepoFile.cs` [SourceCode, ~35 lines]
- `src/AgentWiki.Core/Models/WikiBundle.cs` [SourceCode, ~130 lines]
- `src/AgentWiki.Core/Models/WikiGenerationRequest.cs` [SourceCode, ~40 lines]
- `src/AgentWiki.Core/Models/WikiSection.cs` [SourceCode, ~11 lines]
- `tests/AgentWiki.Cli.Tests/AgentWiki.Cli.Tests.csproj` [Tests, ~37 lines]
- `tests/AgentWiki.Cli.Tests/Analysis/FileCategorizerTests.cs` [Tests, ~42 lines]
- `tests/AgentWiki.Cli.Tests/Analysis/GitIgnoreMatcherTests.cs` [Tests, ~107 lines]
- `tests/AgentWiki.Cli.Tests/Generation/ArchitectureMarkdownRendererTests.cs` [Tests, ~45 lines]
- `tests/AgentWiki.Cli.Tests/Generation/CostEstimatorTests.cs` [Tests, ~31 lines]
- `tests/AgentWiki.Cli.Tests/Integration/EndToEndOfflineTests.cs` [Tests, ~118 lines]
- `tests/AgentWiki.Cli.Tests/Services/AgentBootstrapperTests.cs` [Tests, ~148 lines]
- `tests/AgentWiki.Cli.Tests/Services/ArchitectureGeneratorTests.cs` [Tests, ~192 lines]
- … and 9 more selected file(s)
```
