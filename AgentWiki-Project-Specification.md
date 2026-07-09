# AgentWiki - Project Specification

**Project Name:** AgentWiki  
**CLI Command:** `agent-wiki`  
**Type:** .Net 10 Console / CLI Tool (publishable as `dotnet tool`)  
**Goal:** A native .NET alternative to LangChain's OpenWiki — generates and maintains high-quality, **agent-optimized documentation** (a "wiki") for codebases so that coding agents (GitHub Copilot, custom agents, Claude Code, Cursor, etc.) have persistent, structured context.  
**Target Environment:** Enterprise .NET teams on Azure (fintech example: Elevate Credit)  
**Primary Authoring Agent:** Grok Build CLI (xAI)  
**Date:** July 2026  
**Version of Spec:** 1.0 (Initial Build)

---

## 1. Executive Summary

Build a professional-grade .NET CLI tool that:
- Analyzes a codebase (with smart filtering)
- Uses an LLM (via Semantic Kernel + Azure OpenAI or compatible) to generate structured Markdown documentation optimized for AI coding agents
- Supports one-shot generation and incremental updates (detects git changes)
- Automatically injects usage instructions into `AGENTS.md` / `CLAUDE.md` (or company equivalent)
- Integrates easily into CI/CD (GitHub Actions or Azure DevOps) to keep documentation fresh via PRs
- Is fully native .NET — no LangChain, no JavaScript/TypeScript dependencies in the pipeline

This directly addresses common enterprise problems: stale wikis, poor context for coding agents, and the desire to keep everything inside the Microsoft/.NET/Azure ecosystem.

---

## 2. Goals (v1.0)

- Deliver a working `agent-wiki` CLI that can be installed via `dotnet tool install`
- Generate a useful, navigable wiki in `docs/wiki/` (or configurable path)
- Support both full generation and smart incremental updates using git diffs
- Automatically create/update an `AGENTS.md` file with clear instructions for coding agents
- Provide a clean, professional CLI experience with Spectre.Console
- Include a ready-to-use GitHub Actions workflow example
- Be well-structured, testable, and extensible for internal customization
- Use Semantic Kernel for orchestration (the idiomatic .NET agent framework)
- Prioritize reliability: structured outputs, good error handling, logging

---

## 3. Non-Goals (v1.0 — Explicitly Out of Scope)

- Full web UI or chat interface over the wiki (future phase)
- Vector database / RAG layer inside the wiki generator itself (keep it simple file-based LLM Wiki pattern)
- Multi-repo centralized knowledge base (per-repo focus for v1)
- Automatic diagram generation beyond basic Mermaid (can be added later)
- Support for non-.NET languages as primary (works on any text codebase but optimized for .NET)
- Complex approval workflows or human-in-the-loop editing in v1

---

## 4. Tech Stack (Locked for v1.0)

- **Runtime**: .Net 10
- **Language**: C# 14 (latest language features)
- **Primary AI Framework**: Microsoft.SemanticKernel (latest stable)
- **LLM Access**: Azure OpenAI (via Semantic Kernel connector) — fallback to OpenAI-compatible or GitHub Models
- **CLI Framework**: Spectre.Console + Spectre.Console.Cli (excellent DX, theming, progress bars, tables)
- **Git Integration**: LibGit2Sharp (or `System.Diagnostics.Process` + git CLI for simplicity — recommend starting with Process for fewer dependencies)
- **Configuration**: Microsoft.Extensions.Configuration (JSON + environment variables + optional .env support via dotenv.net or simple parser)
- **Logging**: Serilog (with console + file + optional Application Insights sink)
- **Dependency Injection**: Microsoft.Extensions.DependencyInjection (standard)
- **Testing**: xUnit + Moq + Shouldly (or FluentAssertions)
- **Other**:
  - System.Text.Json (structured outputs)
  - Polly (resilience for LLM calls — retries, circuit breaker)
  - Optional: Azure.Identity for managed identity / DefaultAzureCredential

**Strong Recommendation**: Use **Semantic Kernel** for the generation pipeline. It provides planners, function calling, memory, and clean separation of prompts vs. code — perfect for this multi-step wiki generation task.

---

## 5. CLI Interface (Required Commands)

The tool must support the following:

```bash
# One-time setup
agent-wiki init

# Full generation (one-shot)
agent-wiki generate --repo-path . --output docs/wiki --model gpt-4o

# Incremental update (recommended for CI)
agent-wiki update --repo-path . --output docs/wiki

# Show current config / status
agent-wiki status

# Help
agent-wiki --help
agent-wiki generate --help
```

**Key Options** (examples):
- `--repo-path` / `-r` (default: current directory)
- `--output` / `-o` (default: `docs/wiki`)
- `--config` path to config file
- `--model` / `-m`
- `--provider` (azure-openai, openai, github-models, etc.)
- `--force` (overwrite without confirmation)
- `--dry-run`
- `--verbose` / `-v`

The `init` command should create a `.agentwiki/config.json` (or similar) + sample prompts + `.env.example`.

---

## 6. High-Level Architecture

```
User / CI
   |
   v
agent-wiki CLI (Spectre.Console.Cli)
   |
   +--> Configuration Loader
   |
   +--> RepoAnalyzer (file discovery, .gitignore respect, language detection, size filtering)
   |
   +--> ChangeDetector (git diff or last-run marker)
   |
   +--> WikiGenerationOrchestrator (Semantic Kernel)
   |      |
   |      +--> PromptManager (loads system + task prompts, supports templating)
   |      +--> Structured Output handling (JSON → Markdown)
   |      +--> Hierarchical generation (overview → sections → cross-links)
   |
   +--> OutputWriter (writes Markdown files + index)
   |
   +--> AgentBootstrapper (appends instructions to AGENTS.md / CLAUDE.md)
   |
   +--> Telemetry / Logging (Serilog)
```

**Core Flow for `generate`**:
1. Load config + validate
2. Analyze repository (build file inventory + summary)
3. (Optional) Detect changes since last wiki
4. Run multi-step Semantic Kernel pipeline to generate content
5. Write structured Markdown files
6. Update `AGENTS.md`
7. Report summary (files changed, tokens used, time)

**Core Flow for `update`**:
- Same as above but only re-generate sections impacted by recent code changes (use git diff + mapping to wiki sections).

---

## 7. Proposed Folder Structure (Generated Project)

```
AgentWiki/
├── src/
│   ├── AgentWiki.Cli/                    # Main Spectre.Console.Cli project
│   │   ├── Commands/
│   │   │   ├── GenerateCommand.cs
│   │   │   ├── UpdateCommand.cs
│   │   │   ├── InitCommand.cs
│   │   │   └── StatusCommand.cs
│   │   ├── Services/                     # Injected services
│   │   │   ├── IWikiGenerator.cs
│   │   │   ├── WikiGenerator.cs
│   │   │   ├── IRepoAnalyzer.cs
│   │   │   ├── RepoAnalyzer.cs
│   │   │   ├── IChangeDetector.cs
│   │   │   ├── GitChangeDetector.cs
│   │   │   ├── IPromptManager.cs
│   │   │   ├── PromptManager.cs
│   │   │   ├── IOutputWriter.cs
│   │   │   ├── MarkdownOutputWriter.cs
│   │   │   └── AgentBootstrapper.cs
│   │   ├── Models/
│   │   │   ├── AgentWikiConfig.cs
│   │   │   ├── RepoFile.cs
│   │   │   ├── WikiSection.cs
│   │   │   └── GenerationResult.cs
│   │   ├── Prompts/                      # Embedded or file-based prompt templates
│   │   │   ├── SystemPrompt.txt
│   │   │   ├── ArchitectureOverviewPrompt.txt
│   │   │   ├── ModuleAnalysisPrompt.txt
│   │   │   └── CrossLinkValidationPrompt.txt
│   │   ├── Program.cs
│   │   └── appsettings.json
│   │
│   └── AgentWiki.Core/                   # Shared logic (if needed later)
│
├── .github/
│   └── workflows/
│       └── agent-wiki-update.yml         # Example CI workflow (must be generated or documented)
│
├── docs/
│   └── wiki/                             # Example output (small sample)
│
├── tests/
│   ├── AgentWiki.Cli.Tests/
│   └── AgentWiki.IntegrationTests/
│
├── .editorconfig
├── Directory.Build.props
├── AgentWiki.sln
├── README.md
├── LICENSE (MIT or internal)
└── .gitignore
```

---

## 8. Core Components — Detailed Responsibilities

### 8.1 RepoAnalyzer
- Recursively walks the repository
- Respects `.gitignore` + additional ignore patterns from config (e.g., `node_modules`, `bin/`, `*.min.js`, large binary files)
- Categorizes files: `SourceCode`, `Documentation`, `Configuration`, `Tests`, `Diagrams`, `Other`
- Builds lightweight inventory + basic stats (file count, total lines, major folders)
- Can produce a "repo summary" string for the LLM

### 8.2 ChangeDetector (for `update` mode)
- Uses git to find commits since last successful wiki generation (store `.agentwiki/last-run.json` with commit SHA + timestamp)
- Maps changed files to likely wiki sections (simple heuristics + optional LLM)
- Only re-generates affected sections when possible (big efficiency win)

### 8.3 WikiGenerationOrchestrator (Semantic Kernel)
- Uses a **Kernel** with Azure OpenAI chat completion service
- Implements a multi-step process:
  1. Generate high-level architecture overview
  2. Identify major modules / bounded contexts
  3. Generate detailed content per module
  4. Generate cross-cutting concerns (auth, logging, data access, etc.)
  5. Validate and improve cross-links
  6. Produce final `index.md` + navigation
- Heavy use of **structured outputs** (JSON schema) for reliability
- Supports streaming for better UX in interactive mode

### 8.4 PromptManager
- Loads prompt templates from embedded resources or `Prompts/` folder
- Supports simple variable substitution (`{{RepoName}}`, `{{ChangedFiles}}`, etc.)
- Provides the main system prompt that defines the "agent-wiki style"

### 8.5 OutputWriter
- Writes clean, consistently formatted Markdown
- Creates directory structure
- Generates `index.md` with table of contents / links
- Handles frontmatter if desired (YAML for metadata)

### 8.6 AgentBootstrapper
- Checks for existence of `AGENTS.md` or `CLAUDE.md` in repo root (or configurable)
- Appends (or creates) a standardized block:
  ```markdown
  ## AgentWiki Documentation
  This repository maintains an **agent-optimized wiki** at `docs/wiki/`.

  **For any task involving this codebase:**
  1. Start by reading `docs/wiki/index.md` and `docs/wiki/architecture.md`
  2. Drill into specific modules under `docs/wiki/modules/`
  3. The wiki is kept up-to-date via automated CI. Do not ignore it.
  ```
- Idempotent (doesn't duplicate the block)

---

## 9. Wiki Output Format (Recommended Structure)

Default output location: `docs/wiki/`

```
docs/wiki/
├── index.md                    # Entry point + navigation
├── architecture.md             # High-level system design, layers, key decisions
├── key-components.md           # Major classes, services, projects
├── data-flows.md               # Important request/response flows
├── modules/
│   ├── authentication.md
│   ├── payments.md
│   ├── lending-engine.md
│   └── ...
├── cross-cutting/
│   ├── logging-and-telemetry.md
│   ├── configuration.md
│   └── error-handling.md
├── decisions/                  # ADR-style notes (optional in v1)
├── glossary.md
└── .agentwiki-meta.json        # Internal metadata (last generated commit, etc.)
```

The LLM should be instructed to produce **agent-friendly** content:
- Clear section headers
- Bullet points and numbered lists preferred over dense paragraphs
- Explicit "How to extend / modify" guidance where relevant
- Code snippets with file paths
- Mermaid diagrams for architecture where helpful

---

## 10. Prompting Strategy (Critical — Include Good Defaults)

The tool must ship with high-quality default prompts.

**Example System Prompt (core style guide for the LLM):**

```
You are an expert senior software architect and technical writer specializing in creating documentation optimized for AI coding agents.

Your goal is to produce clear, structured, actionable Markdown that helps coding agents (GitHub Copilot, Claude, Cursor, custom agents) quickly understand the codebase structure, architecture, patterns, and how to make changes safely.

Key principles:
- Be concise but complete.
- Use hierarchical structure with clear headings.
- Prefer bullet points, tables, and short code examples over long prose.
- Always reference actual file paths.
- Highlight important patterns, conventions, and "gotchas".
- Make cross-references explicit (use relative Markdown links).
- Focus on what an agent needs to know to implement features or fix bugs correctly.
```

Then provide task-specific prompts for each generation step (overview, module, etc.).

The spec should include 3-4 ready-to-use prompt templates in the `Prompts/` folder.

---

## 11. Configuration

Support multiple ways (priority order):
1. Command-line arguments (highest)
2. `.agentwiki/config.json` in repo root
3. Environment variables (`AGENTWIKI_*`)
4. `appsettings.json` (for the tool itself)

Example config keys:
- `OutputPath`
- `DefaultModel`
- `AzureOpenAI.Endpoint`, `DeploymentName`, `ApiKey` (or use DefaultAzureCredential)
- `IgnorePatterns` (additional to .gitignore)
- `MaxFilesToAnalyze`
- `EnableIncrementalUpdates`
- `AgentMdPath` (default: `AGENTS.md`)

---

## 12. CI/CD Integration

The project **must** include a high-quality example GitHub Actions workflow at `.github/workflows/agent-wiki-update.yml`

Key behaviors:
- Runs on schedule (e.g., daily at 2am) **and** on push to main (with path filter for code changes)
- Uses `agent-wiki update`
- If changes are detected in the wiki output, the workflow creates a Pull Request with the updates + clear title/description
- Uses `GITHUB_TOKEN` or a PAT with limited permissions
- Reports token usage / cost estimates if possible

Also document how to do the same in Azure DevOps pipelines.

---

## 13. Logging, Observability & Cost Control

- Structured logging with Serilog (include correlation IDs for runs)
- Log key events: files analyzed, sections generated, tokens used (input/output), duration, errors
- Optional Application Insights integration (via config)
- In verbose mode, show progress bars (Spectre) and per-section token counts
- Consider adding a simple cost estimator (rough tokens × model price)

---

## 14. Security & Compliance Notes (Fintech Context)

- Never log API keys or full prompts/responses by default (redact sensitive content)
- Support Managed Identity / DefaultAzureCredential for Azure OpenAI
- All generated documentation stays inside the repository (no external services beyond the LLM call)
- Make prompt templates auditable (checked into source control)
- Add clear disclaimers in generated wiki that content is AI-generated and should be reviewed

---

## 15. Phased Implementation Plan (Recommended for Grok Build)

Because this is a non-trivial project, build it in clear phases inside the Grok Build session:

**Phase 1: Foundation (Skeleton + CLI)**
- Create solution + projects
- Implement `init` and basic `generate` command with Spectre.Console
- Hard-code a simple "hello world" wiki output
- Configuration loading

**Phase 2: Repository Analysis**
- Implement `RepoAnalyzer` with gitignore support
- Build file inventory + basic stats

**Phase 3: Semantic Kernel Integration + Basic Generation**
- Add Semantic Kernel
- Connect to Azure OpenAI (or mock)
- Implement one-pass generation of a single `architecture.md`
- Structured JSON output example

**Phase 4: Full Multi-Step Generation + OutputWriter**
- Implement the full orchestrator with multiple prompt steps
- Proper Markdown writing + index generation
- `AGENTS.md` bootstrap logic

**Phase 5: Incremental Updates + Change Detection**
- Implement `update` command
- Git-based change detection
- Selective regeneration

**Phase 6: Polish, CI Example, Documentation, Testing**
- Add logging, progress, error handling, resilience (Polly)
- Create the GitHub Actions workflow file
- Write excellent README
- Add basic unit + integration tests
- Final CLI UX improvements

Grok Build should confirm each phase before moving to the next.

---

## 16. Acceptance Criteria (Definition of Done for v1.0)

- `dotnet tool install` works and `agent-wiki --version` returns correct info
- `agent-wiki init` creates sensible default config
- `agent-wiki generate` produces a usable `docs/wiki/` folder with multiple .md files on a real .NET codebase
- `agent-wiki update` runs successfully and only processes changed areas when possible
- `AGENTS.md` is created/updated with correct instructions
- The provided GitHub Actions workflow runs and can open a PR with wiki updates
- Good error messages and logging
- Code is clean, follows .NET conventions, and is easy for an internal team to extend

---

## 17. Additional Deliverables (Include in the Generated Project)

1. High-quality `README.md` with:
   - Quick start
   - Architecture overview diagram (Mermaid)
   - Configuration reference
   - How to customize prompts
   - CI setup instructions
   - Comparison with OpenWiki / when to use this vs RAG

2. Example `.agentwiki/config.json`

3. Sample prompt templates (at least 4)

4. `.github/workflows/agent-wiki-update.yml` (fully working example)

5. `CONTRIBUTING.md` or internal notes on how to extend (new sections, different output formats, etc.)

---

## Final Instructions for Grok Build CLI

When building this project, please:

1. Start by creating the solution structure exactly as outlined in Section 7.
2. Follow the phased plan in Section 15. Ask for confirmation before completing each phase.
3. Prioritize clean architecture and dependency injection from the beginning.
4. Use modern C# features (primary constructors, records, pattern matching, etc.).
5. Make the default prompts high-quality and well-commented.
6. Include comprehensive inline documentation (XML comments on public types).
7. After generating code, run `dotnet build` and `dotnet test` to verify everything compiles and basic tests pass.
8. At the end, update the `README.md` with accurate usage examples based on what was actually implemented.

This specification is the single source of truth. Deviate only when there is a clearly better .NET-native approach, and explain the deviation.

---

**End of Specification**

You now have everything needed. Save this file as `AgentWiki-Project-Specification.md` in a new empty directory, run the Grok Build CLI (`grok`), and start with a prompt like:

> "Create a new .Net 10 solution for the AgentWiki CLI tool. Use the detailed specification in `AgentWiki-Project-Specification.md`. Follow the phased implementation plan. Begin with Phase 1."

This should give you a very strong starting point with minimal back-and-forth. 

Let me know if you want me to also generate a shorter "quick start prompt" version or any supporting files (like a sample GitHub Action YAML as a separate file). I'm ready to iterate on this spec if anything needs adjustment before you hand it to Grok Build.