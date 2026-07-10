# Key Components

> Combines architecture components, module map, and inventory.

## Architecture components

- **bump-version.sh** (`.grok/skills/bump-version/scripts/bump-version.sh`): Source file (Shell)
- **pack-and-install-tool.sh** (`scripts/pack-and-install-tool.sh`): Source file (Shell)
- **CommandSettingsBase.cs** (`src/AgentWiki.Cli/Commands/CommandSettingsBase.cs`): Source file (C#)
- **GenerateCommand.cs** (`src/AgentWiki.Cli/Commands/GenerateCommand.cs`): Source file (C#)
- **InitCommand.cs** (`src/AgentWiki.Cli/Commands/InitCommand.cs`): Source file (C#)
- **StatusCommand.cs** (`src/AgentWiki.Cli/Commands/StatusCommand.cs`): Source file (C#)
- **TestProviderCommand.cs** (`src/AgentWiki.Cli/Commands/TestProviderCommand.cs`): Source file (C#)
- **UpdateCommand.cs** (`src/AgentWiki.Cli/Commands/UpdateCommand.cs`): Source file (C#)
- **AgentWikiLogging.cs** (`src/AgentWiki.Cli/Infrastructure/AgentWikiLogging.cs`): Source file (C#)
- **TypeRegistrar.cs** (`src/AgentWiki.Cli/Infrastructure/TypeRegistrar.cs`): Source file (C#)
- **Program.cs** (`src/AgentWiki.Cli/Program.cs`): Source file (C#)
- **AgentBootstrapper.cs** (`src/AgentWiki.Cli/Services/AgentBootstrapper.cs`): Source file (C#)
- **ArchitectureGenerator.cs** (`src/AgentWiki.Cli/Services/ArchitectureGenerator.cs`): Source file (C#)
- **ConfigLoader.cs** (`src/AgentWiki.Cli/Services/ConfigLoader.cs`): Source file (C#)
- **DotEnvLoader.cs** (`src/AgentWiki.Cli/Services/DotEnvLoader.cs`): Source file (C#)

## Modules

- [AgentWiki.Cli](modules/agentwiki-cli.md) — Project module defined by `src/AgentWiki.Cli/AgentWiki.Cli.csproj`.
- [AgentWiki.Core](modules/agentwiki-core.md) — Project module defined by `src/AgentWiki.Core/AgentWiki.Core.csproj`.
- [AgentWiki.Cli.Tests](modules/agentwiki-cli-tests.md) — Project module defined by `tests/AgentWiki.Cli.Tests/AgentWiki.Cli.Tests.csproj`.

## Languages

| Language | Files |
|----------|------:|
| C# | 77 |
| Markdown | 6 |
| Shell | 2 |
| JSON | 2 |
| YAML | 1 |

