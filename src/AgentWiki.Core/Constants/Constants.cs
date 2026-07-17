namespace AgentWiki.Core;

/// <summary>
/// Single source of truth for product identity, paths, config defaults, providers, and limits.
/// Prefer <c>Constants.Group.Name</c> over magic strings/numbers in code.
/// </summary>
public static class Constants
{
    /// <summary>Product / tool identity and version.</summary>
    public static class Product
    {
        public const string ToolName = "agent-wiki";
        public const string ProductName = "AgentWiki";
        public const string Version = "1.3.0";
    }

    /// <summary>Filesystem paths and well-known file names (repo-relative unless noted).</summary>
    public static class Paths
    {
        public const string ConfigDirectoryName = ".agentwiki";
        public const string ConfigFileName = "config.json";
        public const string LastRunFileName = "last-run.json";
        public const string MetaFileName = ".agentwiki-meta.json";
        public const string PromptsDirectoryName = "prompts";

        public const string DefaultOutputPath = "docs/wiki";
        public const string DefaultAgentMdPath = "AGENTS.md";
        public const string DefaultClaudeMdPath = "CLAUDE.md";
        public const string DefaultReadmePath = "README.md";

        public const string EnvFileName = ".env";
        public const string EnvExampleFileName = ".env.example";

        /// <summary>Primary GitHub Copilot instructions path.</summary>
        public const string CopilotInstructionsGithub = ".github/copilot-instructions.md";

        /// <summary>Less common root-level Copilot instructions path.</summary>
        public const string CopilotInstructionsRoot = "copilot-instructions.md";
    }

    /// <summary>Default values for <see cref="Models.AgentWikiConfig"/> and matching scaffolds.</summary>
    public static class Config
    {
        public const string DefaultModel = "gpt-4o";
        public const string DefaultProvider = Providers.AzureOpenAi;

        public const int MaxFilesToAnalyze = 500;
        public const bool EnableIncrementalUpdates = true;

        /// <summary>Per-request LLM HTTP timeout (seconds). Large repos need well beyond HttpClient's 100s default.</summary>
        public const int LlmTimeoutSeconds = 1_200; // 20 minutes

        /// <summary>Max characters of repository summary included in LLM prompts.</summary>
        public const int MaxLlmSummaryChars = 32_000;

        /// <summary>
        /// When true (default), transport/parse failures fall back to offline generators.
        /// Set false for production runs that should fail loudly rather than write inventory-only docs.
        /// </summary>
        public const bool AllowOfflineFallback = true;

        public const bool EnablePostProcessing = true;
        public const string PostProcessingModeLenient = "lenient";
        public const string PostProcessingModeStrict = "strict";
        public const string DefaultPostProcessingMode = PostProcessingModeLenient;

        public const bool EnableRoslynAnalysis = true;
        public const int MaxProjectsToAnalyze = 20;
        public const int MaxSourceFilesForRoslyn = 500;

        public const bool EnableApiEndpointDocs = true;
        public const bool EnableEndpointLlmEnrichment = true;

        public const int MaxModules = 16;
        public const int MaxFilesPerModule = 40;
        public const bool IncludeTestProjectsAsModules = false;

        /// <summary>When true, generate a full AGENTS.md if missing or trivial during wiki generate.</summary>
        public const bool GenerateAgentsMdIfMissing = true;

        /// <summary>When true, generate README.md if missing or detected as a generic template.</summary>
        public const bool GenerateReadmeIfMissingOrGeneric = true;

        /// <summary>When true, migrate and remove well-known copilot-instructions files after AGENTS write.</summary>
        public const bool MigrateCopilotInstructions = true;

        /// <summary>README shorter than this (chars) is treated as generic / empty.</summary>
        public const int ReadmeGenericMaxLength = 500;

        /// <summary>AGENTS.md shorter than this (chars) is treated as trivial / missing content.</summary>
        public const int AgentsMdTrivialMaxLength = 200;

        /// <summary>Default ignore patterns beyond .gitignore (copied into new configs).</summary>
        public static IReadOnlyList<string> DefaultIgnorePatterns { get; } =
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
    }

    /// <summary>Canonical LLM provider identifiers (normalized form).</summary>
    public static class Providers
    {
        public const string AzureOpenAi = "azure-openai";
        public const string OpenAi = "openai";
        public const string GitHubModels = "github-models";
        public const string Offline = "offline";
        public const string Mock = "mock";

        /// <summary>All known providers including offline (e.g. Settings combo).</summary>
        public static IReadOnlyList<string> All { get; } =
        [
            AzureOpenAi,
            OpenAi,
            GitHubModels,
            Offline
        ];

        /// <summary>Providers that can make live chat calls (e.g. Provider test combo).</summary>
        public static IReadOnlyList<string> Live { get; } =
        [
            AzureOpenAi,
            OpenAi,
            GitHubModels
        ];
    }

    /// <summary>AGENTS.md bootstrap markers and related text.</summary>
    public static class AgentsMd
    {
        public const string MarkerBegin = "<!-- BEGIN AGENTWIKI -->";
        public const string MarkerEnd = "<!-- END AGENTWIKI -->";

        /// <summary>Heading used for the mandatory self-updating section (searchable in tests).</summary>
        public const string SelfUpdateSectionHeading = "## Keep this file (and README) up to date";
    }

    /// <summary>Environment variable names and prefixes.</summary>
    public static class Env
    {
        public const string Prefix = "AGENTWIKI_";
        public const string OpenAiApiKey = "OPENAI_API_KEY";
        public const string GitHubToken = "GITHUB_TOKEN";
    }

    /// <summary>LLM client bounds and resilience (not user config defaults — see <see cref="Config"/>).</summary>
    public static class Llm
    {
        public const int MinTimeoutSeconds = 30;
        public const int MaxTimeoutSeconds = 1_800; // allow up to 30 minutes when configured

        /// <summary>Extra seconds allowed on the outer HttpClient beyond the per-request network timeout.</summary>
        public const int HttpClientTimeoutSlackSeconds = 30;

        /// <summary>Absolute cap for HttpClient.Timeout when applying slack.</summary>
        public const int AbsoluteMaxHttpClientTimeoutSeconds = 1_830;

        /// <summary>Retry attempts after the first try (total attempts = MaxRetryAttempts + 1).</summary>
        public const int MaxRetryAttempts = 4;

        public const int RetryBaseDelaySeconds = 2;
    }

    /// <summary>Analysis / inventory / generation limits not exposed as config (or fallbacks).</summary>
    public static class Analysis
    {
        /// <summary>Hard safety cap so pathological trees cannot exhaust memory.</summary>
        public const int AbsoluteFileCap = 50_000;

        /// <summary>Default max size for line counting (larger files are still inventoried).</summary>
        public const long DefaultMaxLineCountBytes = 512 * 1024;

        /// <summary>Changed-file count above which incremental update forces full regeneration.</summary>
        public const int FullRegenerationFileThreshold = 40;

        public const int FullSummaryMaxSelectedFiles = 80;
        public const int LlmSummaryMaxSelectedFiles = 40;

        public const int ObsoleteScanMaxFiles = 40;
        public const int EndpointTableMaxRows = 40;
    }

    /// <summary>Desktop / companion UI defaults.</summary>
    public static class Ui
    {
        public const int MaxRecentRepos = 10;
        public const int LogTailLines = 200;
    }
}
