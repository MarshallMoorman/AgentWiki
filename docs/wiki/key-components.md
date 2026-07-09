# Key Components

> File map derived from repository analysis (Phase 2).

## Languages

| Language | Files |
|----------|------:|
| C# | 33 |
| Markdown | 2 |
| JSON | 1 |

## Selected source files

- `src/AgentWiki.Cli/Commands/CommandSettingsBase.cs` (~52 lines)
- `src/AgentWiki.Cli/Commands/GenerateCommand.cs` (~138 lines)
- `src/AgentWiki.Cli/Commands/InitCommand.cs` (~61 lines)
- `src/AgentWiki.Cli/Commands/StatusCommand.cs` (~201 lines)
- `src/AgentWiki.Cli/Commands/UpdateCommand.cs` (~61 lines)
- `src/AgentWiki.Cli/Infrastructure/TypeRegistrar.cs` (~44 lines)
- `src/AgentWiki.Cli/Program.cs` (~90 lines)
- `src/AgentWiki.Cli/Services/ConfigLoader.cs` (~201 lines)
- `src/AgentWiki.Cli/Services/InitService.cs` (~194 lines)
- `src/AgentWiki.Cli/Services/MarkdownOutputWriter.cs` (~72 lines)
- `src/AgentWiki.Cli/Services/PlaceholderWikiGenerator.cs` (~380 lines)
- `src/AgentWiki.Cli/Services/RepoAnalyzer.cs` (~410 lines)
- `src/AgentWiki.Core/Abstractions/IConfigLoader.cs` (~32 lines)
- `src/AgentWiki.Core/Abstractions/IInitService.cs` (~34 lines)
- `src/AgentWiki.Core/Abstractions/IOutputWriter.cs` (~19 lines)
- `src/AgentWiki.Core/Abstractions/IRepoAnalyzer.cs` (~17 lines)
- `src/AgentWiki.Core/Abstractions/IWikiGenerator.cs` (~17 lines)
- `src/AgentWiki.Core/Analysis/FileCategorizer.cs` (~184 lines)
- `src/AgentWiki.Core/Analysis/GitIgnoreMatcher.cs` (~299 lines)
- `src/AgentWiki.Core/Analysis/RepoSummaryBuilder.cs` (~95 lines)
- `src/AgentWiki.Core/Constants/AgentWikiConstants.cs` (~28 lines)
- `src/AgentWiki.Core/Models/AgentWikiConfig.cs` (~76 lines)
- `src/AgentWiki.Core/Models/GenerationResult.cs` (~66 lines)
- `src/AgentWiki.Core/Models/RepoAnalysisResult.cs` (~43 lines)
- `src/AgentWiki.Core/Models/RepoFile.cs` (~35 lines)
- `src/AgentWiki.Core/Models/WikiGenerationRequest.cs` (~34 lines)
- `src/AgentWiki.Core/Models/WikiSection.cs` (~11 lines)

## Selected configuration

- `.editorconfig`
- `.gitignore`
- `AgentWiki.slnx`
- `Directory.Build.props`
- `src/AgentWiki.Cli/AgentWiki.Cli.csproj`
- `src/AgentWiki.Cli/appsettings.json`
- `src/AgentWiki.Core/AgentWiki.Core.csproj`

## Test projects / files (sample)

- `tests/AgentWiki.Cli.Tests/AgentWiki.Cli.Tests.csproj`
- `tests/AgentWiki.Cli.Tests/Analysis/FileCategorizerTests.cs`
- `tests/AgentWiki.Cli.Tests/Analysis/GitIgnoreMatcherTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/ConfigLoaderTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/InitServiceTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/PlaceholderWikiGeneratorTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/RepoAnalyzerTests.cs`
