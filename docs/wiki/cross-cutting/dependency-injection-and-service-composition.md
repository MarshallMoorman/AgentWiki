# Dependency Injection and Service Composition

> Current cross-cutting documentation (AI-assisted).

## Summary

Application services are registered centrally and composed through dependency injection, enabling testability and consistent startup behavior.

## Patterns

- Service registration extensions
- Constructor-injected dependencies
- Centralized application bootstrap
- Separation of orchestration and implementation services

## Key files

- `src/AgentWiki.App/ServiceCollectionExtensions.cs`
- `src/AgentWiki.App/Services/AgentBootstrapper.cs`

## Guidance for agents

- Register new services through the existing service collection extension patterns.
- Prefer constructor injection over service location.
- Keep orchestration logic in bootstrapper-level services and isolate feature-specific behavior into dedicated services.
- Design new services to be mockable for tests.

## Navigation

- [Wiki index](../index.md)
- [Architecture](../architecture.md)
