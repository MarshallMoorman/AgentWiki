# Examples & Configuration

> Current module documentation for coding agents (AI-assisted).

## Purpose

Provides reference configuration and CI/CD workflow examples that show how to configure and run AgentWiki from external repositories and automation pipelines.

## Entry points

- `examples/agentwiki.config.json`
- `examples/github-actions/agent-wiki-update.yml`
- `examples/azure-pipelines/agent-wiki-update.yml`

## Dependencies / roots

- `AgentWiki configuration schema`
- `GitHub Actions workflow runtime`
- `Azure Pipelines YAML runtime`

## Key types / files

- examples/agentwiki.config.json
- examples/github-actions/agent-wiki-update.yml
- examples/azure-pipelines/agent-wiki-update.yml

## Endpoints / Public API

_No HTTP or Function endpoints discovered for this module._

## How to extend

- Update examples/agentwiki.config.json when configuration options change so the reference configuration stays aligned with current behavior.
- Add new CI/CD examples alongside the existing workflow files and keep usage patterns consistent across providers.
- When introducing new required inputs or settings, reflect them in both GitHub Actions and Azure Pipelines examples.
- Use repository-relative paths and configuration values that are safe to copy into external repositories.

## Gotchas

- These files are examples and integration templates; changes can affect how users copy and adopt AgentWiki in external repositories.
- Keep configuration examples synchronized with the actual supported configuration fields.
- Ensure GitHub Actions and Azure Pipelines examples remain functionally equivalent when documenting the same workflow.
- Do not include secrets, tokens, or environment-specific values in example files.

## Related files

- `examples/agentwiki.config.json`
- `examples/github-actions/agent-wiki-update.yml`
- `examples/azure-pipelines/agent-wiki-update.yml`

## Navigation

- [Wiki index](../index.md)
- [Architecture](../architecture.md)
- [API Endpoints](../api-endpoints.md)
