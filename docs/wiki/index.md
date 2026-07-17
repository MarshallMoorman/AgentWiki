# agent-wiki — AgentWiki

> Agent-optimized documentation for the **current** codebase.

## Navigation

| Page | Description |
|------|-------------|
| [Architecture](architecture.md) | System design, layers, decisions |
| [API Endpoints](api-endpoints.md) | HTTP / Function route catalog |
| [Key Components](key-components.md) | Component map |
| [Data Flows](data-flows.md) | Important request/process flows |
| [Repository Inventory](inventory.md) | File inventory summary |
| [Glossary](glossary.md) | Terms and abbreviations |
| [Getting Started](getting-started.md) | Agent usage guide |

### Modules

| Module | Purpose |
|--------|---------|
| [AgentWiki Application](modules/agentwiki-app.md) | Primary application project that coordinates repository analysis, configuration loading, git change detection, AI prompt execution, dependency injection setup, and generation of agent-oriented wiki/documentation outputs. |
| [Prompt Templates](modules/prompt-library.md) | Catalog of text prompt templates used by the application to instruct LLMs for architecture analysis, module discovery and analysis, implementation planning, cross-cutting concern analysis, cross-link validation, and wiki content generation. |
| [Infrastructure & Telemetry](modules/application-infrastructure.md) | Provides cross-cutting application infrastructure for logging, telemetry, environment-variable loading, and Git process execution used by the application. |
| [Automated Tests](modules/test-suite.md) | Provides repository-wide test coverage for CLI generation workflows, analysis utilities, orchestration services, desktop application services, view models, configuration handling, resilience behavior, rendering logic, and end-to-end offline wiki generation scenarios. The test projects act as executable specifications for expected behavior and are the primary safety net when modifying generation, analysis, service, or UI logic. |
| [Documentation & Product Specification](modules/documentation-specification.md) | Human-authored project documentation that defines product goals, requirements, architecture direction, development workflows, agent-oriented guidance, handoff information, and planned future work for the Agent Wiki repository. |
| [Examples & Configuration](modules/examples-and-config.md) | Provides reference configuration and CI/CD workflow examples that show how to configure and run AgentWiki from external repositories and automation pipelines. |
| [Automation, Packaging & CI](modules/automation-and-ci.md) | Provides repository automation for continuous integration, tool packaging/installation, desktop execution, version-bump workflows, and wiki refresh operations through GitHub Actions and supporting scripts. |

### Cross-cutting

| Topic | Summary |
|-------|---------|
| [Configuration and Environment Loading](cross-cutting/configuration-and-environment-loading.md) | Application behavior is driven by configuration files, environment variables, and optional .env loading. Configuration is validated and merged before services execute. |
| [Logging and Telemetry](cross-cutting/logging-and-telemetry.md) | Operational visibility is provided through centralized logging and optional telemetry collection for application runs. |
| [Dependency Injection and Service Composition](cross-cutting/dependency-injection-and-service-composition.md) | Application services are registered centrally and composed through dependency injection, enabling testability and consistent startup behavior. |
| [AI Prompt Driven Document Generation](cross-cutting/ai-prompt-driven-document-generation.md) | Documentation generation relies on prompt templates and generator services that analyze repositories and produce agent-oriented outputs. |
| [Git and Repository Analysis](cross-cutting/git-and-repository-analysis.md) | Repository inspection, change detection, and Git integration are core capabilities used to scope generation work and understand project state. |
| [Error Handling and Resilience](cross-cutting/error-handling-and-resilience.md) | The application performs external operations such as file access, Git execution, configuration loading, and AI interactions, requiring consistent failure handling and diagnostics. |
| [Testing and Quality Gates](cross-cutting/testing-and-quality-gates.md) | The repository includes a substantial automated test suite and CI workflows that act as the primary quality gate for changes. |
| [Automation and Release Workflows](cross-cutting/automation-and-release-workflows.md) | Repository maintenance, wiki generation, packaging, and version-related activities are automated through scripts and workflow definitions. |

## Quick facts

- **Repository:** `agent-wiki`
- **Generated at (UTC):** 2026-07-17T10:12:09.6031470+00:00
- **Files (after ignores):** 203
- **Selected for analysis:** 203
- **Approx. lines:** 27,988
- **Modules documented:** 7
- **Generation mode:** LLM-assisted
- **Correlation ID:** `27a5e1eb0d9244fbb307b8b30922ec50`

## How to use this wiki

1. Read [architecture.md](architecture.md) for the current system shape.
2. Check [api-endpoints.md](api-endpoints.md) for HTTP / Function routes.
3. Open the relevant page under [modules/](modules/) or [cross-cutting/](cross-cutting/).
4. Use [inventory.md](inventory.md) when you need exact paths.
5. Treat this as a map of the live tree; confirm critical details in source when implementing.
