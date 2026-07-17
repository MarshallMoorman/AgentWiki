using AgentWiki.Core;
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
        (provider ?? Constants.Config.DefaultProvider).Trim().ToLowerInvariant() switch
        {
            "azure" or "aoai" or "azureopenai" or Constants.Providers.AzureOpenAi
                => Constants.Providers.AzureOpenAi,
            "openai" or "oai" or Constants.Providers.OpenAi
                => Constants.Providers.OpenAi,
            "github" or "githubmodels" or Constants.Providers.GitHubModels
                => Constants.Providers.GitHubModels,
            "none" or Constants.Providers.Mock or Constants.Providers.Offline
                => Constants.Providers.Offline,
            var p when string.IsNullOrWhiteSpace(p) => Constants.Config.DefaultProvider,
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
        var specific = provider is Constants.Providers.OpenAi or Constants.Providers.GitHubModels
            ? config.OpenAI.Model
            : config.AzureOpenAI.DeploymentName;

        if (!string.IsNullOrWhiteSpace(specific))
        {
            return specific.Trim();
        }

        return string.IsNullOrWhiteSpace(config.DefaultModel)
            ? Constants.Config.DefaultModel
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
            Constants.Providers.Offline or Constants.Providers.Mock => "provider is offline/mock",
            Constants.Providers.AzureOpenAi when config.AzureOpenAI.UseManagedIdentity
                && string.IsNullOrWhiteSpace(config.AzureOpenAI.Endpoint)
                => "Azure endpoint not set (managed identity)",
            Constants.Providers.AzureOpenAi when !config.AzureOpenAI.UseManagedIdentity
                && (string.IsNullOrWhiteSpace(config.AzureOpenAI.Endpoint)
                    || string.IsNullOrWhiteSpace(config.AzureOpenAI.ApiKey))
                => "Azure endpoint and/or API key not set",
            Constants.Providers.OpenAi or Constants.Providers.GitHubModels
                when string.IsNullOrWhiteSpace(config.OpenAI.ApiKey)
                => "OpenAI API key not set (openAI.apiKey, .env, AGENTWIKI_OpenAI__ApiKey, AGENTWIKI_ApiKey, or OPENAI_API_KEY)",
            _ => null
        };
    }
}
