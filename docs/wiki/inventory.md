# Repository Inventory

> Machine-generated from RepoAnalyzer.

```text
# Repository: agent-wiki
Path: /Users/mmoorman/dev/ea/agent-wiki

## Inventory summary
- Total files (after ignores): 102
- Selected for analysis: 102
- Total size: 409.8 KB
- Approximate lines (text files): 11,506

## Files by category
- SourceCode: 59
- Documentation: 13
- Configuration: 9
- Tests: 21
- Diagrams: 0
- Other: 0

## Detected languages
- C#: 77 file(s)
- Markdown: 6 file(s)
- Shell: 2 file(s)
- JSON: 2 file(s)
- YAML: 1 file(s)

## Top folders
- `src/` — 66 files (294.3 KB)
- `tests/` — 21 files (63.7 KB)
- `(root)/` — 9 files (34.7 KB)
- `.grok/` — 2 files (3.9 KB)
- `.github/` — 1 files (3.4 KB)
- `docs/` — 1 files (7.8 KB)
- `examples/` — 1 files (693 B)
- `scripts/` — 1 files (1.3 KB)

## Top extensions
- .cs: 77
- .md: 6
- .txt: 6
- .csproj: 3
- .sh: 2
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
- `.grok/skills/bump-version/scripts/bump-version.sh` [SourceCode, ~68 lines]
- `.grok/skills/bump-version/SKILL.md` [Documentation, ~60 lines]
- `AGENTS.md` [Documentation, ~76 lines]
- `AgentWiki-Project-Specification.md` [Documentation, ~476 lines]
- `AgentWiki.slnx` [Configuration, ~9 lines]
- `CONTRIBUTING.md` [Documentation, ~80 lines]
- `Directory.Build.props` [Configuration, ~21 lines]
- `docs/HANDOFF.md` [Documentation, ~198 lines]
- `examples/agentwiki.config.json` [Configuration, ~32 lines]
- `LICENSE` [Documentation, ~21 lines]
- `README.md` [Documentation, ~191 lines]
- `scripts/pack-and-install-tool.sh` [SourceCode, ~40 lines]
- `src/AgentWiki.Cli/AgentWiki.Cli.csproj` [Configuration, ~53 lines]
- `src/AgentWiki.Cli/appsettings.json` [Configuration, ~32 lines]
- `src/AgentWiki.Cli/Commands/CommandSettingsBase.cs` [SourceCode, ~52 lines]
- `src/AgentWiki.Cli/Commands/GenerateCommand.cs` [SourceCode, ~171 lines]
- `src/AgentWiki.Cli/Commands/InitCommand.cs` [SourceCode, ~61 lines]
- `src/AgentWiki.Cli/Commands/StatusCommand.cs` [SourceCode, ~250 lines]
- `src/AgentWiki.Cli/Commands/TestProviderCommand.cs` [SourceCode, ~172 lines]
- `src/AgentWiki.Cli/Commands/UpdateCommand.cs` [SourceCode, ~61 lines]
- `src/AgentWiki.Cli/Infrastructure/AgentWikiLogging.cs` [SourceCode, ~116 lines]
- `src/AgentWiki.Cli/Infrastructure/TypeRegistrar.cs` [SourceCode, ~44 lines]
- `src/AgentWiki.Cli/Program.cs` [SourceCode, ~118 lines]
- `src/AgentWiki.Cli/Prompts/ArchitectureOverviewPrompt.txt` [Documentation, ~33 lines]
- `src/AgentWiki.Cli/Prompts/CrossCuttingPrompt.txt` [Documentation, ~21 lines]
- `src/AgentWiki.Cli/Prompts/CrossLinkValidationPrompt.txt` [Documentation, ~17 lines]
- `src/AgentWiki.Cli/Prompts/ModuleAnalysisPrompt.txt` [Documentation, ~24 lines]
- `src/AgentWiki.Cli/Prompts/ModulePlanPrompt.txt` [Documentation, ~25 lines]
- `src/AgentWiki.Cli/Prompts/SystemPrompt.txt` [Documentation, ~19 lines]
- `src/AgentWiki.Cli/Services/AgentBootstrapper.cs` [SourceCode, ~150 lines]
- `src/AgentWiki.Cli/Services/ArchitectureGenerator.cs` [SourceCode, ~296 lines]
- `src/AgentWiki.Cli/Services/ConfigLoader.cs` [SourceCode, ~209 lines]
- `src/AgentWiki.Cli/Services/DotEnvLoader.cs` [SourceCode, ~80 lines]
- `src/AgentWiki.Cli/Services/GitChangeDetector.cs` [SourceCode, ~438 lines]
- `src/AgentWiki.Cli/Services/GitProcess.cs` [SourceCode, ~73 lines]
- `src/AgentWiki.Cli/Services/InitService.cs` [SourceCode, ~244 lines]
- `src/AgentWiki.Cli/Services/LastRunStore.cs` [SourceCode, ~59 lines]
- `src/AgentWiki.Cli/Services/LlmResilience.cs` [SourceCode, ~66 lines]
- `src/AgentWiki.Cli/Services/MarkdownOutputWriter.cs` [SourceCode, ~72 lines]
- `src/AgentWiki.Cli/Services/PlaceholderWikiGenerator.cs` [SourceCode, ~380 lines]
- `src/AgentWiki.Cli/Services/PromptManager.cs` [SourceCode, ~104 lines]
- `src/AgentWiki.Cli/Services/RepoAnalyzer.cs` [SourceCode, ~410 lines]
- `src/AgentWiki.Cli/Services/SemanticKernelLlmCompletionService.cs` [SourceCode, ~387 lines]
- `src/AgentWiki.Cli/Services/SemanticWikiGenerator.cs` [SourceCode, ~346 lines]
- `src/AgentWiki.Cli/Services/WikiGenerationOrchestrator.cs` [SourceCode, ~787 lines]
- `src/AgentWiki.Core/Abstractions/IAgentBootstrapper.cs` [SourceCode, ~42 lines]
- `src/AgentWiki.Core/Abstractions/IArchitectureGenerator.cs` [SourceCode, ~19 lines]
- `src/AgentWiki.Core/Abstractions/IChangeDetector.cs` [SourceCode, ~29 lines]
- `src/AgentWiki.Core/Abstractions/IConfigLoader.cs` [SourceCode, ~32 lines]
- `src/AgentWiki.Core/Abstractions/IInitService.cs` [SourceCode, ~34 lines]
- `src/AgentWiki.Core/Abstractions/ILlmCompletionService.cs` [SourceCode, ~64 lines]
- `src/AgentWiki.Core/Abstractions/IOutputWriter.cs` [SourceCode, ~19 lines]
- `src/AgentWiki.Core/Abstractions/IPromptManager.cs` [SourceCode, ~18 lines]
- `src/AgentWiki.Core/Abstractions/IRepoAnalyzer.cs` [SourceCode, ~17 lines]
- `src/AgentWiki.Core/Abstractions/IWikiGenerationOrchestrator.cs` [SourceCode, ~20 lines]
- `src/AgentWiki.Core/Abstractions/IWikiGenerator.cs` [SourceCode, ~17 lines]
- `src/AgentWiki.Core/AgentWiki.Core.csproj` [Configuration, ~10 lines]
- `src/AgentWiki.Core/Analysis/FileCategorizer.cs` [SourceCode, ~184 lines]
- `src/AgentWiki.Core/Analysis/GitIgnoreMatcher.cs` [SourceCode, ~299 lines]
- `src/AgentWiki.Core/Analysis/PromptText.cs` [SourceCode, ~24 lines]
- `src/AgentWiki.Core/Analysis/RepoSummaryBuilder.cs` [SourceCode, ~115 lines]
- `src/AgentWiki.Core/Constants/AgentWikiConstants.cs` [SourceCode, ~28 lines]
- `src/AgentWiki.Core/Generation/ArchitectureMarkdownRenderer.cs` [SourceCode, ~171 lines]
- `src/AgentWiki.Core/Generation/CostEstimator.cs` [SourceCode, ~66 lines]
- `src/AgentWiki.Core/Generation/LlmJson.cs` [SourceCode, ~272 lines]
- `src/AgentWiki.Core/Generation/ModuleMarkdownRenderer.cs` [SourceCode, ~229 lines]
- `src/AgentWiki.Core/Generation/OfflineArchitectureGenerator.cs` [SourceCode, ~140 lines]
- `src/AgentWiki.Core/Generation/OfflineModulePlanner.cs` [SourceCode, ~288 lines]
- `src/AgentWiki.Core/Generation/TokenUsageMath.cs` [SourceCode, ~27 lines]
- `src/AgentWiki.Core/Models/AgentWikiConfig.cs` [SourceCode, ~90 lines]
- `src/AgentWiki.Core/Models/ArchitectureDocument.cs` [SourceCode, ~89 lines]
- `src/AgentWiki.Core/Models/GenerationResult.cs` [SourceCode, ~82 lines]
- `src/AgentWiki.Core/Models/LastRunState.cs` [SourceCode, ~89 lines]
- `src/AgentWiki.Core/Models/RepoAnalysisResult.cs` [SourceCode, ~43 lines]
- `src/AgentWiki.Core/Models/RepoFile.cs` [SourceCode, ~35 lines]
- `src/AgentWiki.Core/Models/WikiBundle.cs` [SourceCode, ~130 lines]
- `src/AgentWiki.Core/Models/WikiGenerationRequest.cs` [SourceCode, ~40 lines]
- … and 22 more selected file(s)
```
