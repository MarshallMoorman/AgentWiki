# Logging and Telemetry

> Offline / inventory-derived cross-cutting notes. Verify against source.

## Summary

Logging/telemetry-related files and conventions inferred from inventory. No strongly matching files were found; treat this as baseline guidance.

## Patterns

- Use structured logging with correlation IDs for multi-step runs.
- Never log secrets, API keys, or full prompt/response payloads by default.

## Key files

_None listed._

## Guidance for agents

- Add log events around external calls and generation pipeline steps.
- Prefer warning/error levels for actionable failures.

## Navigation

- [Wiki index](../index.md)
- [Architecture](../architecture.md)
