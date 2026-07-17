# Prompt Templates

> Current module documentation for coding agents (AI-assisted).

## Purpose

Catalog of text prompt templates used by the application to instruct LLMs for architecture analysis, module discovery and analysis, implementation planning, cross-cutting concern analysis, cross-link validation, and wiki content generation.

## Entry points

- `src/AgentWiki.App/Prompts/SystemPrompt.txt`
- `src/AgentWiki.App/Prompts/ArchitectureOverviewPrompt.txt`
- `src/AgentWiki.App/Prompts/ModuleAnalysisPrompt.txt`
- `src/AgentWiki.App/Prompts/ModulePlanPrompt.txt`
- `src/AgentWiki.App/Prompts/CrossCuttingPrompt.txt`
- `src/AgentWiki.App/Prompts/CrossLinkValidationPrompt.txt`

## Dependencies / roots

- `LLM prompt-loading and execution flow in AgentWiki.App`
- `Repository inventory and analysis context supplied to prompts`
- `Wiki generation pipeline consuming prompt outputs`

## Key types / files

- src/AgentWiki.App/Prompts/SystemPrompt.txt
- src/AgentWiki.App/Prompts/ArchitectureOverviewPrompt.txt
- src/AgentWiki.App/Prompts/ModuleAnalysisPrompt.txt
- src/AgentWiki.App/Prompts/ModulePlanPrompt.txt
- src/AgentWiki.App/Prompts/CrossCuttingPrompt.txt
- src/AgentWiki.App/Prompts/CrossLinkValidationPrompt.txt

## Endpoints / Public API

_No HTTP or Function endpoints discovered for this module._

## How to extend

- Update the specific prompt file that corresponds to the behavior being changed instead of modifying unrelated prompts.
- Keep instructions consistent with SystemPrompt.txt so downstream prompts operate under the same expectations and output conventions.
- When introducing new analysis requirements, add explicit instructions to the relevant prompt template and preserve existing structure expected by consuming code.
- Use repository-relative file paths in prompt instructions to match project documentation conventions.
- Validate that changes to one prompt do not conflict with assumptions made by architecture, module, cross-cutting, or cross-link analysis prompts.

## Gotchas

- Prompt files act as behavioral contracts for LLM output; changing wording can alter generated content and downstream processing.
- Maintain alignment across prompt templates to avoid inconsistent terminology or output formats.
- CrossLinkValidationPrompt.txt should remain focused on link validation concerns and not absorb unrelated analysis instructions.
- Changes to SystemPrompt.txt can affect every prompt execution that incorporates it.
- Prompt outputs may be consumed by automation, so avoid unnecessary format drift when editing templates.

## Related files

- `src/AgentWiki.App/Prompts/SystemPrompt.txt`
- `src/AgentWiki.App/Prompts/ArchitectureOverviewPrompt.txt`
- `src/AgentWiki.App/Prompts/ModuleAnalysisPrompt.txt`
- `src/AgentWiki.App/Prompts/ModulePlanPrompt.txt`
- `src/AgentWiki.App/Prompts/CrossCuttingPrompt.txt`
- `src/AgentWiki.App/Prompts/CrossLinkValidationPrompt.txt`

## Navigation

- [Wiki index](../index.md)
- [Architecture](../architecture.md)
- [API Endpoints](../api-endpoints.md)
