namespace AgentWiki.Core.Models;

/// <summary>
/// Root configuration for an AgentWiki run.
/// Priority (highest wins): CLI flags → repo <c>.env</c> → <c>.agentwiki/config.json</c>
/// → process <c>AGENTWIKI_*</c> env → tool appsettings.
/// </summary>
public sealed class AgentWikiConfig
{
    /// <summary>Absolute or relative path to the repository root.</summary>
    public string RepoPath { get; set; } = ".";

    /// <summary>Wiki output directory relative to the repo root (default: <c>docs/wiki</c>).</summary>
    public string OutputPath { get; set; } = "docs/wiki";

    /// <summary>Default LLM model / deployment name.</summary>
    public string DefaultModel { get; set; } = "gpt-4o";

    /// <summary>LLM provider identifier (e.g. azure-openai, openai, github-models).</summary>
    public string Provider { get; set; } = "azure-openai";

    /// <summary>Path to the agent bootstrap markdown file (default: <c>AGENTS.md</c>).</summary>
    public string AgentMdPath { get; set; } = "AGENTS.md";

    /// <summary>Maximum number of source files to analyze.</summary>
    public int MaxFilesToAnalyze { get; set; } = 500;

    /// <summary>Whether incremental (git-diff-based) updates are enabled.</summary>
    public bool EnableIncrementalUpdates { get; set; } = true;

    /// <summary>
    /// Per-request HTTP timeout for LLM calls (seconds). Default 300 (5 minutes).
    /// Large repos can exceed the .NET HttpClient default of 100s.
    /// </summary>
    public int LlmTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Max characters of repository summary included in LLM prompts (default 16_000).
    /// Keeps prompts bounded so requests finish within the timeout.
    /// </summary>
    public int MaxLlmSummaryChars { get; set; } = 16_000;

    /// <summary>
    /// When true (default), run <c>IWikiPostProcessor</c> guardrails after generation steps
    /// (absolute path rewrite, dependency cleanup, deprecation neutralization, link hygiene).
    /// </summary>
    public bool EnablePostProcessing { get; set; } = true;

    /// <summary>
    /// Post-processing strictness: <c>lenient</c> (default) rewrites suspect language;
    /// <c>strict</c> drops unverified deprecation claims more aggressively.
    /// </summary>
    public string PostProcessingMode { get; set; } = "lenient";

    /// <summary>
    /// When true (default), run optional Roslyn syntax analysis on C# sources for richer offline wikis.
    /// Gracefully skipped for non-.NET repos or on failure.
    /// </summary>
    public bool EnableRoslynAnalysis { get; set; } = true;

    /// <summary>Max .csproj/.fsproj projects to treat as analysis roots (default 20).</summary>
    public int MaxProjectsToAnalyze { get; set; } = 20;

    /// <summary>Max C# source files to parse with Roslyn (default 200).</summary>
    public int MaxSourceFilesForRoslyn { get; set; } = 200;

    /// <summary>
    /// When true (default), emit <c>api-endpoints.md</c> and per-module endpoint sections
    /// from static analysis (and optional LLM descriptions).
    /// </summary>
    public bool EnableApiEndpointDocs { get; set; } = true;

    /// <summary>
    /// When true (default) and LLM credentials are available, request short endpoint descriptions.
    /// Offline catalog still ships without LLM.
    /// </summary>
    public bool EnableEndpointLlmEnrichment { get; set; } = true;

    /// <summary>
    /// Glob-like include filters for endpoint routes or source paths (empty = include all).
    /// Matched against route, handler, and relative path (case-insensitive substring or <c>*</c> wildcard).
    /// </summary>
    public List<string> EndpointIncludePatterns { get; set; } = [];

    /// <summary>
    /// Glob-like exclude filters for endpoint routes or source paths (e.g. <c>*/swagger*</c>, <c>/health*</c>).
    /// </summary>
    public List<string> EndpointExcludePatterns { get; set; } = [];

    /// <summary>Additional ignore patterns beyond <c>.gitignore</c>.</summary>
    public List<string> IgnorePatterns { get; set; } =
    [
        "**/bin/**",
        "**/obj/**",
        "**/node_modules/**",
        "**/.git/**",
        "**/packages/**",
        "**/*.min.js",
        "**/*.min.css",
        "**/docs/wiki/**",
        "**/.agentwiki/**"
    ];

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
