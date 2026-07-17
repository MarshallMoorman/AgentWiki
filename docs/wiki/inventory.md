# Repository Inventory

> Machine-generated from RepoAnalyzer.

```text
# Repository: agent-wiki
Path: .

## Inventory summary
- Total files (after ignores): 203
- Selected for analysis: 203
- Total size: 1.0 MB
- Approximate lines (text files): 27,988

## Files by category
- SourceCode: 113
- Documentation: 17
- Configuration: 19
- Tests: 40
- Diagrams: 0
- Other: 14

## Detected languages
- C#: 152 file(s)
- Markdown: 10 file(s)
- YAML: 4 file(s)
- Shell: 3 file(s)
- JSON: 3 file(s)

## Top folders
- `src/` — 140 files (751.6 KB)
- `tests/` — 40 files (166.0 KB)
- `(root)/` — 9 files (46.6 KB)
- `docs/` — 5 files (49.5 KB)
- `examples/` — 3 files (13.0 KB)
- `.github/` — 2 files (5.4 KB)
- `.grok/` — 2 files (3.9 KB)
- `scripts/` — 2 files (3.5 KB)

## Top extensions
- .cs: 152
- .axaml: 13
- .md: 10
- .csproj: 6
- .txt: 6
- .yml: 4
- .sh: 3
- .json: 3
- .editorconfig: 1
- .gitignore: 1
- .slnx: 1
- .props: 1

## Selected files (sample for analysis)
- `.editorconfig` [Configuration, ~28 lines]
- `.github/workflows/ci.yml` [Configuration, ~72 lines]
- `.github/workflows/wiki-refresh.yml` [Configuration, ~88 lines]
- `.gitignore` [Configuration, ~48 lines]
- `.grok/skills/bump-version/scripts/bump-version.sh` [SourceCode, ~68 lines]
- `.grok/skills/bump-version/SKILL.md` [Documentation, ~60 lines]
- `AGENTS.md` [Documentation, ~94 lines]
- `AgentWiki-Project-Specification.md` [Documentation, ~476 lines]
- `AgentWiki.slnx` [Configuration, ~12 lines]
- `CONTRIBUTING.md` [Documentation, ~100 lines]
- `Directory.Build.props` [Configuration, ~21 lines]
- `docs/development/01-agents-readme-generation-prompt.md` [Documentation, ~60 lines]
- `docs/development/01-agents-readme-generation-requirements.md` [Documentation, ~226 lines]
- `docs/HANDOFF.md` [Documentation, ~123 lines]
- `docs/plans/docs-plan-single-repo-polish-v1.2.md` [Documentation, ~251 lines]
- `docs/plans/ui-companion-avalonia.md` [Documentation, ~433 lines]
- `examples/agentwiki.config.json` [Configuration, ~49 lines]
- `examples/azure-pipelines/agent-wiki-update.yml` [Configuration, ~205 lines]
- `examples/github-actions/agent-wiki-update.yml` [Configuration, ~101 lines]
- `LICENSE` [Documentation, ~21 lines]
- `README.md` [Documentation, ~321 lines]
- `scripts/pack-and-install-tool.sh` [SourceCode, ~104 lines]
- `scripts/run-desktop.sh` [SourceCode, ~16 lines]
- `src/AgentWiki.App/AgentWiki.App.csproj` [Configuration, ~42 lines]
- `src/AgentWiki.App/Infrastructure/AgentWikiLogging.cs` [Configuration, ~95 lines]
- `src/AgentWiki.App/Infrastructure/ApplicationInsightsRunTelemetry.cs` [Configuration, ~188 lines]
- `src/AgentWiki.App/Prompts/ArchitectureOverviewPrompt.txt` [Documentation, ~35 lines]
- `src/AgentWiki.App/Prompts/CrossCuttingPrompt.txt` [Documentation, ~21 lines]
- `src/AgentWiki.App/Prompts/CrossLinkValidationPrompt.txt` [Documentation, ~17 lines]
- `src/AgentWiki.App/Prompts/ModuleAnalysisPrompt.txt` [Documentation, ~24 lines]
- `src/AgentWiki.App/Prompts/ModulePlanPrompt.txt` [Documentation, ~30 lines]
- `src/AgentWiki.App/Prompts/SystemPrompt.txt` [Documentation, ~20 lines]
- `src/AgentWiki.App/ServiceCollectionExtensions.cs` [SourceCode, ~44 lines]
- `src/AgentWiki.App/Services/AgentBootstrapper.cs` [SourceCode, ~150 lines]
- `src/AgentWiki.App/Services/AgentsMdGenerator.cs` [SourceCode, ~318 lines]
- `src/AgentWiki.App/Services/ArchitectureGenerator.cs` [SourceCode, ~317 lines]
- `src/AgentWiki.App/Services/ConfigLoader.cs` [SourceCode, ~432 lines]
- `src/AgentWiki.App/Services/DotEnvLoader.cs` [SourceCode, ~133 lines]
- `src/AgentWiki.App/Services/GitChangeDetector.cs` [SourceCode, ~439 lines]
- `src/AgentWiki.App/Services/GitProcess.cs` [SourceCode, ~73 lines]
- `src/AgentWiki.App/Services/InitService.cs` [SourceCode, ~247 lines]
- `src/AgentWiki.App/Services/InstructionFileDiscovery.cs` [SourceCode, ~111 lines]
- `src/AgentWiki.App/Services/LastRunStore.cs` [SourceCode, ~59 lines]
- `src/AgentWiki.App/Services/LlmResilience.cs` [SourceCode, ~140 lines]
- `src/AgentWiki.App/Services/MarkdownOutputWriter.cs` [SourceCode, ~125 lines]
- `src/AgentWiki.App/Services/PlaceholderWikiGenerator.cs` [SourceCode, ~385 lines]
- `src/AgentWiki.App/Services/PromptManager.cs` [SourceCode, ~108 lines]
- `src/AgentWiki.App/Services/ReadmeGenerator.cs` [SourceCode, ~85 lines]
- `src/AgentWiki.App/Services/RepoAnalyzer.cs` [SourceCode, ~422 lines]
- `src/AgentWiki.App/Services/SemanticKernelLlmCompletionService.cs` [SourceCode, ~424 lines]
- `src/AgentWiki.App/Services/SemanticWikiGenerator.cs` [SourceCode, ~551 lines]
- `src/AgentWiki.App/Services/WikiGenerationOrchestrator.cs` [SourceCode, ~1087 lines]
- `src/AgentWiki.Cli/AgentWiki.Cli.csproj` [Configuration, ~45 lines]
- `src/AgentWiki.Cli/appsettings.json` [Configuration, ~32 lines]
- `src/AgentWiki.Cli/Commands/AgentsCommand.cs` [SourceCode, ~173 lines]
- `src/AgentWiki.Cli/Commands/CommandSettingsBase.cs` [SourceCode, ~52 lines]
- `src/AgentWiki.Cli/Commands/GenerateCommand.cs` [SourceCode, ~270 lines]
- `src/AgentWiki.Cli/Commands/InitCommand.cs` [SourceCode, ~63 lines]
- `src/AgentWiki.Cli/Commands/StatusCommand.cs` [SourceCode, ~297 lines]
- `src/AgentWiki.Cli/Commands/TestProviderCommand.cs` [SourceCode, ~173 lines]
- `src/AgentWiki.Cli/Commands/UpdateCommand.cs` [SourceCode, ~70 lines]
- `src/AgentWiki.Cli/Infrastructure/CliConsole.cs` [Configuration, ~36 lines]
- `src/AgentWiki.Cli/Infrastructure/TypeRegistrar.cs` [Configuration, ~44 lines]
- `src/AgentWiki.Cli/Program.cs` [SourceCode, ~116 lines]
- `src/AgentWiki.Core/Abstractions/IAgentBootstrapper.cs` [SourceCode, ~42 lines]
- `src/AgentWiki.Core/Abstractions/IAgentsMdGenerator.cs` [SourceCode, ~17 lines]
- `src/AgentWiki.Core/Abstractions/IArchitectureGenerator.cs` [SourceCode, ~19 lines]
- `src/AgentWiki.Core/Abstractions/IChangeDetector.cs` [SourceCode, ~29 lines]
- `src/AgentWiki.Core/Abstractions/IConfigLoader.cs` [SourceCode, ~32 lines]
- `src/AgentWiki.Core/Abstractions/IInitService.cs` [SourceCode, ~34 lines]
- `src/AgentWiki.Core/Abstractions/ILlmCompletionService.cs` [SourceCode, ~64 lines]
- `src/AgentWiki.Core/Abstractions/IOutputWriter.cs` [SourceCode, ~19 lines]
- `src/AgentWiki.Core/Abstractions/IPromptManager.cs` [SourceCode, ~18 lines]
- `src/AgentWiki.Core/Abstractions/IReadmeGenerator.cs` [SourceCode, ~14 lines]
- `src/AgentWiki.Core/Abstractions/IRepoAnalyzer.cs` [SourceCode, ~17 lines]
- `src/AgentWiki.Core/Abstractions/IRunTelemetry.cs` [SourceCode, ~15 lines]
- `src/AgentWiki.Core/Abstractions/IStaticAnalyzer.cs` [SourceCode, ~19 lines]
- `src/AgentWiki.Core/Abstractions/IWikiGenerationOrchestrator.cs` [SourceCode, ~20 lines]
- `src/AgentWiki.Core/Abstractions/IWikiGenerator.cs` [SourceCode, ~17 lines]
- `src/AgentWiki.Core/Abstractions/IWikiPostProcessor.cs` [SourceCode, ~38 lines]
- … and 123 more selected file(s)
```
