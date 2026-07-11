using AgentWiki.Core.Constants;
using AgentWiki.Core.Models;

namespace AgentWiki.Core.Analysis;

/// <summary>
/// Shared resolution of provider/model for status, generate, and LLM calls.
/// Nested <c>openAI.model</c> / <c>azureOpenAI.deploymentName</c> only win when non-empty;
/// otherwise <see cref="AgentWikiConfig.DefaultModel"/> is used.
/// </summary>
public static class LlmSettings
{
    public static string NormalizeProvider(string? provider) =>
        (provider ?? AgentWikiConstants.DefaultProvider).Trim().ToLowerInvariant() switch
        {
            "azure" or "aoai" or "azureopenai" or "azure-openai" => "azure-openai",
            "openai" or "oai" => "openai",
            "github" or "github-models" or "githubmodels" => "github-models",
            "mock" or "offline" or "none" => "offline",
            var p when string.IsNullOrWhiteSpace(p) => AgentWikiConstants.DefaultProvider,
            var p => p
        };

    /// <summary>
    /// Model / deployment actually used for chat completions.
    /// Priority: CLI override → provider-specific non-empty model → <c>defaultModel</c> → built-in default.
    /// </summary>
    public static string ResolveModel(
        AgentWikiConfig config,
        string? modelOverride = null,
        string? providerOverride = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!string.IsNullOrWhiteSpace(modelOverride))
        {
            return modelOverride.Trim();
        }

        var provider = NormalizeProvider(providerOverride ?? config.Provider);
        var specific = provider is "openai" or "github-models"
            ? config.OpenAI.Model
            : config.AzureOpenAI.DeploymentName;

        if (!string.IsNullOrWhiteSpace(specific))
        {
            return specific.Trim();
        }

        return string.IsNullOrWhiteSpace(config.DefaultModel)
            ? AgentWikiConstants.DefaultModel
            : config.DefaultModel.Trim();
    }

    /// <summary>
    /// Human-readable reason LLM cannot run live (empty when ready).
    /// </summary>
    public static string? DescribeNotReadyReason(AgentWikiConfig config, string? providerOverride = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var provider = NormalizeProvider(providerOverride ?? config.Provider);

        return provider switch
        {
            "offline" or "mock" => "provider is offline/mock",
            "azure-openai" when config.AzureOpenAI.UseManagedIdentity
                && string.IsNullOrWhiteSpace(config.AzureOpenAI.Endpoint)
                => "Azure endpoint not set (managed identity)",
            "azure-openai" when !config.AzureOpenAI.UseManagedIdentity
                && (string.IsNullOrWhiteSpace(config.AzureOpenAI.Endpoint)
                    || string.IsNullOrWhiteSpace(config.AzureOpenAI.ApiKey))
                => "Azure endpoint and/or API key not set",
            "openai" or "github-models" when string.IsNullOrWhiteSpace(config.OpenAI.ApiKey)
                => "OpenAI API key not set (openAI.apiKey, .env, AGENTWIKI_OpenAI__ApiKey, AGENTWIKI_ApiKey, or OPENAI_API_KEY)",
            _ => null
        };
    }
}
