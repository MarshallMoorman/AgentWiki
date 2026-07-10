# Configuration

> Cross-cutting notes derived from the current file inventory.

## Summary

Configuration, project settings, Policies/ (APIM), and pipeline definitions from the inventory.

## Patterns

- Prefer environment-specific overrides over hard-coded values.
- Keep secrets out of committed config; use env vars or secret stores.
- Treat Policies/ XML and azure-*-pipeline YAML as part of the deployment surface, not optional noise.

## Key files

- `.editorconfig`
- `.github/workflows/agent-wiki-update.yml`
- `.gitignore`
- `AgentWiki.slnx`
- `Directory.Build.props`
- `examples/agentwiki.config.json`
- `src/AgentWiki.Cli/AgentWiki.Cli.csproj`
- `src/AgentWiki.Cli/appsettings.json`
- `src/AgentWiki.Cli/Infrastructure/AgentWikiLogging.cs`
- `src/AgentWiki.Cli/Infrastructure/TypeRegistrar.cs`
- `src/AgentWiki.Core/AgentWiki.Core.csproj`

## Guidance for agents

- Update `.agentwiki/config.json` for AgentWiki settings.
- Document new config keys next to the code that consumes them.
- When changing public APIs, check Policies/ and pipeline YAML for required deploy updates.

## Navigation

- [Wiki index](../index.md)
- [Architecture](../architecture.md)
