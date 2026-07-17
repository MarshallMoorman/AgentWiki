# AI Prompt Driven Document Generation

> Current cross-cutting documentation (AI-assisted).

## Summary

Documentation generation relies on prompt templates and generator services that analyze repositories and produce agent-oriented outputs.

## Patterns

- Prompt files stored as application assets
- Generator services per document type
- Structured AI interactions
- Repository-analysis workflows

## Key files

- `src/AgentWiki.App/Prompts/SystemPrompt.txt`
- `src/AgentWiki.App/Prompts/ArchitectureOverviewPrompt.txt`
- `src/AgentWiki.App/Prompts/CrossCuttingPrompt.txt`
- `src/AgentWiki.App/Prompts/ModuleAnalysisPrompt.txt`
- `src/AgentWiki.App/Prompts/ModulePlanPrompt.txt`
- `src/AgentWiki.App/Prompts/CrossLinkValidationPrompt.txt`
- `src/AgentWiki.App/Services/AgentsMdGenerator.cs`
- `src/AgentWiki.App/Services/ArchitectureGenerator.cs`

## Guidance for agents

- Update prompt templates and generators together when changing output formats.
- Keep generated content grounded in repository inventory and analysis data.
- Avoid hard-coding prompt text in service implementations when a prompt asset already exists.
- Maintain deterministic document structure where downstream automation depends on generated output.

## Navigation

- [Wiki index](../index.md)
- [Architecture](../architecture.md)
