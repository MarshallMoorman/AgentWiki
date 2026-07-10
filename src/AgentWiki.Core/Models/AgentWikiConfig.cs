namespace AgentWiki.Core.Models;

/// <summary>
/// Root configuration for an AgentWiki run.
/// Loaded from CLI args, <c>.agentwiki/config.json</c>, environment variables, and appsettings.
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
