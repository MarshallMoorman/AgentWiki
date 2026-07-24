using AgentWiki.Core.Models;

namespace AgentWiki.Core.Generation;

/// <summary>
/// Builds single-repo <see cref="AgentWikiConfig"/> templates for <c>agent-wiki init</c>
/// and workspace <c>memberDefaults</c> scaffold / replace-configs.
/// </summary>
public static class AgentWikiConfigDefaults
{
    /// <summary>
    /// Bare-minimum config for <c>.agentwiki/config.json</c>: OpenAI provider, default model,
    /// and wiki output path. API keys are expected from <c>OPENAI_API_KEY</c> / process env,
    /// repo <c>.env</c>, or an explicit config override — not scaffolded into the committed file.
    /// </summary>
    public static AgentWikiConfig CreateMinimalTemplate() => new()
    {
        Provider = Constants.Config.DefaultProvider,
        DefaultModel = Constants.Config.DefaultModel,
        OutputPath = Constants.Paths.DefaultOutputPath
    };

    /// <summary>
    /// Full config surface with every property set to product defaults (and provider placeholders).
    /// Used for <c>config.example.json</c>, workspace <c>memberDefaults</c>, and replace-configs.
    /// Nested provider objects include empty apiKey placeholders so the JSON shape is complete.
    /// </summary>
    public static AgentWikiConfig CreateFullTemplate() => new()
    {
        RepoPath = ".",
        OutputPath = Constants.Paths.DefaultOutputPath,
        DefaultModel = Constants.Config.DefaultModel,
        Provider = Constants.Config.DefaultProvider,
        AgentMdPath = Constants.Paths.DefaultAgentMdPath,
        GenerateAgentsMdIfMissing = Constants.Config.GenerateAgentsMdIfMissing,
        GenerateReadmeIfMissingOrGeneric = Constants.Config.GenerateReadmeIfMissingOrGeneric,
        MigrateCopilotInstructions = Constants.Config.MigrateCopilotInstructions,
        ReadmeGenericMaxLength = Constants.Config.ReadmeGenericMaxLength,
        AgentsMdTrivialMaxLength = Constants.Config.AgentsMdTrivialMaxLength,
        MaxFilesToAnalyze = Constants.Config.MaxFilesToAnalyze,
        EnableIncrementalUpdates = Constants.Config.EnableIncrementalUpdates,
        LlmTimeoutSeconds = Constants.Config.LlmTimeoutSeconds,
        MaxLlmSummaryChars = Constants.Config.MaxLlmSummaryChars,
        AllowOfflineFallback = Constants.Config.AllowOfflineFallback,
        EnablePostProcessing = Constants.Config.EnablePostProcessing,
        PostProcessingMode = Constants.Config.DefaultPostProcessingMode,
        EnableRoslynAnalysis = Constants.Config.EnableRoslynAnalysis,
        MaxProjectsToAnalyze = Constants.Config.MaxProjectsToAnalyze,
        MaxSourceFilesForRoslyn = Constants.Config.MaxSourceFilesForRoslyn,
        EnableApiEndpointDocs = Constants.Config.EnableApiEndpointDocs,
        EnableEndpointLlmEnrichment = Constants.Config.EnableEndpointLlmEnrichment,
        EndpointIncludePatterns = [],
        EndpointExcludePatterns = [],
        MaxModules = Constants.Config.MaxModules,
        MaxFilesPerModule = Constants.Config.MaxFilesPerModule,
        ModuleRoots = [],
        ModuleGlobs = [],
        IncludeTestProjectsAsModules = Constants.Config.IncludeTestProjectsAsModules,
        ApplicationInsightsConnectionString = null,
        InputUsdPerMillionTokens = null,
        OutputUsdPerMillionTokens = null,
        ModelPricing = new Dictionary<string, ModelPricingEntry>(StringComparer.OrdinalIgnoreCase),
        IgnorePatterns = [..Constants.Config.DefaultIgnorePatterns],
        AzureOpenAI = new AzureOpenAiOptions
        {
            Endpoint = "https://YOUR_RESOURCE.openai.azure.com/",
            DeploymentName = Constants.Config.DefaultModel,
            ApiKey = "",
            UseManagedIdentity = false
        },
        OpenAI = new OpenAiOptions
        {
            Endpoint = "",
            ApiKey = "",
            Model = Constants.Config.DefaultModel
        }
    };

    /// <summary>
    /// Clone suitable for writing into a member repo: full template with <c>repoPath</c> forced to <c>.</c>.
    /// </summary>
    public static AgentWikiConfig CloneForMember(AgentWikiConfig source)
    {
        ArgumentNullException.ThrowIfNull(source);
        // Deep-enough copy via serialize (AgentWikiConfig is JSON-serializable).
        var json = System.Text.Json.JsonSerializer.Serialize(source, CloneOptions);
        var copy = System.Text.Json.JsonSerializer.Deserialize<AgentWikiConfig>(json, CloneOptions)
                   ?? CreateFullTemplate();
        copy.RepoPath = ".";
        return copy;
    }

    /// <summary>
    /// Returns true if any apiKey-like field is non-empty (for warn-on-load; never log values).
    /// </summary>
    public static bool HasSecretsConfigured(AgentWikiConfig? config)
    {
        if (config is null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(config.AzureOpenAI?.ApiKey)
               || !string.IsNullOrWhiteSpace(config.OpenAI?.ApiKey)
               || LooksLikeConnectionStringSecret(config.ApplicationInsightsConnectionString);
    }

    /// <summary>Human-readable secret presence summary (no values).</summary>
    public static IReadOnlyList<string> DescribeSecretsPresent(AgentWikiConfig? config)
    {
        if (config is null)
        {
            return [];
        }

        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(config.AzureOpenAI?.ApiKey))
        {
            list.Add("azureOpenAI.apiKey is set in memberDefaults");
        }

        if (!string.IsNullOrWhiteSpace(config.OpenAI?.ApiKey))
        {
            list.Add("openAI.apiKey is set in memberDefaults");
        }

        if (LooksLikeConnectionStringSecret(config.ApplicationInsightsConnectionString))
        {
            list.Add("applicationInsightsConnectionString is set in memberDefaults");
        }

        return list;
    }

    private static bool LooksLikeConnectionStringSecret(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (value.Contains("InstrumentationKey", StringComparison.OrdinalIgnoreCase) || value.Length > 20);

    private static readonly System.Text.Json.JsonSerializerOptions CloneOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}
