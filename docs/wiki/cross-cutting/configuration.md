# Configuration

> Offline / inventory-derived cross-cutting notes. Verify against source.

## Summary

Configuration files and project settings discovered in the inventory.

## Patterns

- Prefer environment-specific overrides over hard-coded values.
- Keep secrets out of committed config; use env vars or secret stores.

## Key files

- `.editorconfig`
- `.gitignore`
- `AgentWiki.slnx`
- `Directory.Build.props`
- `src/AgentWiki.Cli/AgentWiki.Cli.csproj`
- `src/AgentWiki.Cli/appsettings.json`
- `src/AgentWiki.Core/AgentWiki.Core.csproj`

## Guidance for agents

- Update `.agentwiki/config.json` for AgentWiki settings.
- Document new config keys next to the code that consumes them.

## Navigation

- [Wiki index](../index.md)
- [Architecture](../architecture.md)
