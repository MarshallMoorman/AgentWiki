# Automated Tests

> Current module documentation for coding agents (AI-assisted).

## Purpose

Provides repository-wide test coverage for CLI generation workflows, analysis utilities, orchestration services, desktop application services, view models, configuration handling, resilience behavior, rendering logic, and end-to-end offline wiki generation scenarios. The test projects act as executable specifications for expected behavior and are the primary safety net when modifying generation, analysis, service, or UI logic.

## Entry points

- `tests/AgentWiki.Cli.Tests/AgentWiki.Cli.Tests.csproj`
- `tests/AgentWiki.Desktop.Tests/AgentWiki.Desktop.Tests.csproj`
- `tests/AgentWiki.Cli.Tests/Integration/EndToEndOfflineTests.cs`

## Dependencies / roots

- `AgentWiki.Cli test project`
- `AgentWiki.Desktop test project`
- `Analysis components validated by tests under tests/AgentWiki.Cli.Tests/Analysis`
- `Generation components validated by tests under tests/AgentWiki.Cli.Tests/Generation`
- `Service-layer components validated by tests under tests/AgentWiki.Cli.Tests/Services`
- `Desktop services and view models validated by tests under tests/AgentWiki.Desktop.Tests`

## Key types / files

- EnvConfigApplierTests
- FileCategorizerTests
- GitIgnoreMatcherTests
- LlmSettingsTests
- PathUtilityTests
- PromptTextTests
- RoslynStaticAnalyzerTests
- AgentsMdAndReadmeTests
- ApiEndpointsMarkdownRendererTests
- ArchitectureMarkdownRendererTests
- CostEstimatorTests
- LlmJsonTests
- ModuleMarkdownRendererTests
- OfflineModulePlannerTests
- WikiPostProcessorTests
- EndToEndOfflineTests
- AgentBootstrapperTests
- ArchitectureGeneratorTests
- ConfigLoaderTests
- DotEnvLoaderTests
- GitChangeDetectorTests
- InitServiceTests
- LastRunStoreTests
- LlmResilienceTests
- MarkdownOutputWriterTests
- PlaceholderWikiGeneratorTests
- PromptManagerTests
- RepoAnalyzerTests
- SemanticKernelSettingsTests
- SemanticKernelTimeoutTests
- SemanticWikiGeneratorTests
- WikiGenerationOrchestratorTests
- ConfigEditorServiceTests
- ThemeServiceTests
- UiSettingsStoreTests
- CommandCanExecuteRefreshTests
- GenerateViewModelTests
- SetupViewModelTests

## Endpoints / Public API

_No HTTP or Function endpoints discovered for this module._

## How to extend

- Add new tests to the existing functional area folder (Analysis, Generation, Services, Integration, ViewModels, or Desktop Services) that matches the component being changed.
- When introducing new generation or rendering behavior, add focused tests alongside existing renderer and generator tests before updating implementation.
- Update end-to-end coverage in tests/AgentWiki.Cli.Tests/Integration/EndToEndOfflineTests.cs when workflow-level behavior changes.
- Add regression tests for bug fixes in the corresponding test class area to preserve expected behavior.
- Keep service-level changes covered by tests under tests/AgentWiki.Cli.Tests/Services and UI-related changes covered by tests under tests/AgentWiki.Desktop.Tests.
- Use existing test files in the same category as examples for naming, organization, and expected assertions.

## Gotchas

- Generation changes can affect multiple test groups, including renderer, generator, orchestrator, and integration tests.
- Configuration-related behavior is validated in several dedicated test files; changes to settings loading or environment handling may require updates across multiple tests.
- Offline generation workflows are covered by integration tests, so changes that appear local can surface as end-to-end failures.
- Desktop view model behavior is verified separately from service behavior; update both layers' tests when modifying UI interaction logic.
- Prompt, LLM configuration, timeout, resilience, and semantic-generation behavior each have dedicated test coverage that may fail when changing AI workflow logic.

## Related files

- `tests/AgentWiki.Cli.Tests/AgentWiki.Cli.Tests.csproj`
- `tests/AgentWiki.Cli.Tests/Analysis/EnvConfigApplierTests.cs`
- `tests/AgentWiki.Cli.Tests/Analysis/FileCategorizerTests.cs`
- `tests/AgentWiki.Cli.Tests/Analysis/GitIgnoreMatcherTests.cs`
- `tests/AgentWiki.Cli.Tests/Analysis/LlmSettingsTests.cs`
- `tests/AgentWiki.Cli.Tests/Analysis/PathUtilityTests.cs`
- `tests/AgentWiki.Cli.Tests/Analysis/PromptTextTests.cs`
- `tests/AgentWiki.Cli.Tests/Analysis/RoslynStaticAnalyzerTests.cs`
- `tests/AgentWiki.Cli.Tests/Generation/AgentsMdAndReadmeTests.cs`
- `tests/AgentWiki.Cli.Tests/Generation/ApiEndpointsMarkdownRendererTests.cs`
- `tests/AgentWiki.Cli.Tests/Generation/ArchitectureMarkdownRendererTests.cs`
- `tests/AgentWiki.Cli.Tests/Generation/CostEstimatorTests.cs`
- `tests/AgentWiki.Cli.Tests/Generation/LlmJsonTests.cs`
- `tests/AgentWiki.Cli.Tests/Generation/ModuleMarkdownRendererTests.cs`
- `tests/AgentWiki.Cli.Tests/Generation/OfflineModulePlannerTests.cs`
- `tests/AgentWiki.Cli.Tests/Generation/WikiPostProcessorTests.cs`
- `tests/AgentWiki.Cli.Tests/Integration/EndToEndOfflineTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/AgentBootstrapperTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/ArchitectureGeneratorTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/ConfigLoaderTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/DotEnvLoaderTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/GitChangeDetectorTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/InitServiceTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/LastRunStoreTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/LlmResilienceTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/MarkdownOutputWriterTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/PlaceholderWikiGeneratorTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/PromptManagerTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/RepoAnalyzerTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/SemanticKernelSettingsTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/SemanticKernelTimeoutTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/SemanticWikiGeneratorTests.cs`
- `tests/AgentWiki.Cli.Tests/Services/WikiGenerationOrchestratorTests.cs`
- `tests/AgentWiki.Desktop.Tests/AgentWiki.Desktop.Tests.csproj`
- `tests/AgentWiki.Desktop.Tests/Services/ConfigEditorServiceTests.cs`
- `tests/AgentWiki.Desktop.Tests/Services/ThemeServiceTests.cs`
- `tests/AgentWiki.Desktop.Tests/Services/UiSettingsStoreTests.cs`
- `tests/AgentWiki.Desktop.Tests/ViewModels/CommandCanExecuteRefreshTests.cs`
- `tests/AgentWiki.Desktop.Tests/ViewModels/GenerateViewModelTests.cs`
- `tests/AgentWiki.Desktop.Tests/ViewModels/SetupViewModelTests.cs`

## Navigation

- [Wiki index](../index.md)
- [Architecture](../architecture.md)
- [API Endpoints](../api-endpoints.md)
