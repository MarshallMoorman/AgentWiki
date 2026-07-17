// Constants lives in AgentWiki.Core (not Models).
// Property defaults must stay aligned with Constants.Config / Constants.Paths.

using Constants = AgentWiki.Core.Constants;

namespace AgentWiki.Core.Models;

/// <summary>
/// Root configuration for an AgentWiki run.
/// Priority (highest wins): CLI flags → repo <c>.env</c> → <c>.agentwiki/config.json</c>
/// → process <c>AGENTWIKI_*</c> env → tool appsettings.
/// Defaults come from <see cref="Constants"/> so values stay in one place.
/// </summary>
public sealed class AgentWikiConfig
{
    /// <summary>Absolute or relative path to the repository root.</summary>
    public string RepoPath { get; set; } = ".";

    /// <summary>Wiki output directory relative to the repo root (default: <c>docs/wiki</c>).</summary>
    public string OutputPath { get; set; } = Constants.Paths.DefaultOutputPath;

    /// <summary>Default LLM model / deployment name.</summary>
    public string DefaultModel { get; set; } = Constants.Config.DefaultModel;

    /// <summary>LLM provider identifier (e.g. azure-openai, openai, github-models).</summary>
    public string Provider { get; set; } = Constants.Config.DefaultProvider;

    /// <summary>Path to the agent bootstrap markdown file (default: <c>AGENTS.md</c>).</summary>
    public string AgentMdPath { get; set; } = Constants.Paths.DefaultAgentMdPath;

    /// <summary>Maximum number of source files to analyze.</summary>
    public int MaxFilesToAnalyze { get; set; } = Constants.Config.MaxFilesToAnalyze;

    /// <summary>Whether incremental (git-diff-based) updates are enabled.</summary>
    public bool EnableIncrementalUpdates { get; set; } = Constants.Config.EnableIncrementalUpdates;

    /// <summary>
    /// Per-request HTTP timeout for LLM calls (seconds). Default 300 (5 minutes).
    /// Large repos can exceed the .NET HttpClient default of 100s.
    /// </summary>
    public int LlmTimeoutSeconds { get; set; } = Constants.Config.LlmTimeoutSeconds;

    /// <summary>
    /// Max characters of repository summary included in LLM prompts (default 16_000).
    /// Keeps prompts bounded so requests finish within the timeout.
    /// </summary>
    public int MaxLlmSummaryChars { get; set; } = Constants.Config.MaxLlmSummaryChars;

    /// <summary>
    /// When true (default), run <c>IWikiPostProcessor</c> guardrails after generation steps
    /// (absolute path rewrite, dependency cleanup, deprecation neutralization, link hygiene).
    /// </summary>
    public bool EnablePostProcessing { get; set; } = Constants.Config.EnablePostProcessing;

    /// <summary>
    /// Post-processing strictness: <c>lenient</c> (default) rewrites suspect language;
    /// <c>strict</c> drops unverified deprecation claims more aggressively.
    /// </summary>
    public string PostProcessingMode { get; set; } = Constants.Config.DefaultPostProcessingMode;

    /// <summary>
    /// When true (default), run optional Roslyn syntax analysis on C# sources for richer offline wikis.
    /// Gracefully skipped for non-.NET repos or on failure.
    /// </summary>
    public bool EnableRoslynAnalysis { get; set; } = Constants.Config.EnableRoslynAnalysis;

    /// <summary>Max .csproj/.fsproj projects to treat as analysis roots (default 20).</summary>
    public int MaxProjectsToAnalyze { get; set; } = Constants.Config.MaxProjectsToAnalyze;

    /// <summary>Max C# source files to parse with Roslyn (default 200).</summary>
    public int MaxSourceFilesForRoslyn { get; set; } = Constants.Config.MaxSourceFilesForRoslyn;

    /// <summary>
    /// When true (default), emit <c>api-endpoints.md</c> and per-module endpoint sections
    /// from static analysis (and optional LLM descriptions).
    /// </summary>
    public bool EnableApiEndpointDocs { get; set; } = Constants.Config.EnableApiEndpointDocs;

    /// <summary>
    /// When true (default) and LLM credentials are available, request short endpoint descriptions.
    /// Offline catalog still ships without LLM.
    /// </summary>
    public bool EnableEndpointLlmEnrichment { get; set; } = Constants.Config.EnableEndpointLlmEnrichment;

    /// <summary>
    /// Glob-like include filters for endpoint routes or source paths (empty = include all).
    /// Matched against route, handler, and relative path (case-insensitive substring or <c>*</c> wildcard).
    /// </summary>
    public List<string> EndpointIncludePatterns { get; set; } = [];

    /// <summary>
    /// Glob-like exclude filters for endpoint routes or source paths (e.g. <c>*/swagger*</c>, <c>/health*</c>).
    /// </summary>
    public List<string> EndpointExcludePatterns { get; set; } = [];

    /// <summary>
    /// Maximum modules to plan/document (offline + LLM). Default 16 (was hard-coded 8).
    /// </summary>
    public int MaxModules { get; set; } = Constants.Config.MaxModules;

    /// <summary>Maximum related files listed per module (default 40).</summary>
    public int MaxFilesPerModule { get; set; } = Constants.Config.MaxFilesPerModule;

    /// <summary>
    /// Explicit module root directories (repo-relative), e.g. <c>src/Api</c>, <c>src/Domain</c>.
    /// When set, these are preferred over automatic discovery (still merged with projects).
    /// </summary>
    public List<string> ModuleRoots { get; set; } = [];

    /// <summary>
    /// Glob-like patterns for module roots (e.g. <c>src/*/</c>, <c>services/*/</c>).
    /// Matched against inventory directories / project folders.
    /// </summary>
    public List<string> ModuleGlobs { get; set; } = [];

    /// <summary>
    /// When false (default), test projects are deprioritized and only included if under the module cap.
    /// When true, test projects compete equally with app libraries.
    /// </summary>
    public bool IncludeTestProjectsAsModules { get; set; } = Constants.Config.IncludeTestProjectsAsModules;

    /// <summary>
    /// Optional Application Insights connection string. When set, run summaries are sent as custom events.
    /// Off by default (null/empty).
    /// </summary>
    public string? ApplicationInsightsConnectionString { get; set; }

    /// <summary>
    /// Optional global input token price (USD per 1M tokens) overriding built-in tables.
    /// Used with <see cref="OutputUsdPerMillionTokens"/> when both are set.
    /// </summary>
    public decimal? InputUsdPerMillionTokens { get; set; }

    /// <summary>
    /// Optional global output token price (USD per 1M tokens) overriding built-in tables.
    /// </summary>
    public decimal? OutputUsdPerMillionTokens { get; set; }

    /// <summary>
    /// Optional per-model pricing overrides (key = model / deployment name fragment).
    /// </summary>
    public Dictionary<string, ModelPricingEntry> ModelPricing { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Additional ignore patterns beyond <c>.gitignore</c>.</summary>
    public List<string> IgnorePatterns { get; set; } = [..Constants.Config.DefaultIgnorePatterns];

    /// <summary>Azure OpenAI connection settings.</summary>
    public AzureOpenAiOptions AzureOpenAI { get; set; } = new();

    /// <summary>OpenAI-compatible endpoint settings (fallback provider).</summary>
    public OpenAiOptions OpenAI { get; set; } = new();
}

/// <summary>Azure OpenAI connection options.</summary>
public sealed class AzureOpenAiOptions
{
    /// <summary>Azure OpenAI resource endpoint (e.g. https://my-resource.openai.azure.com/).</summary>
    public string? Endpoint { get; set; }

    /// <summary>Deployment name for chat completions.</summary>
    public string? DeploymentName { get; set; }

    /// <summary>API key. Prefer managed identity / DefaultAzureCredential when possible.</summary>
    public string? ApiKey { get; set; }

    /// <summary>When true, use DefaultAzureCredential instead of ApiKey.</summary>
    public bool UseManagedIdentity { get; set; }
}

/// <summary>OpenAI-compatible API options.</summary>
public sealed class OpenAiOptions
{
    /// <summary>API base URL (defaults to OpenAI public endpoint when null).</summary>
    public string? Endpoint { get; set; }

    /// <summary>API key.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Model name.</summary>
    public string? Model { get; set; }
}

/// <summary>Per-model USD pricing for cost estimates (not billing-grade).</summary>
public sealed class ModelPricingEntry
{
    /// <summary>USD per 1M input tokens.</summary>
    public decimal InputUsdPerMillion { get; set; }

    /// <summary>USD per 1M output tokens.</summary>
    public decimal OutputUsdPerMillion { get; set; }
}
