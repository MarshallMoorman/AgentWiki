using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Models;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Polly;

namespace AgentWiki.Cli.Services;

/// <summary>
/// Semantic Kernel-backed chat completion for Azure OpenAI and OpenAI-compatible endpoints,
/// with Polly retries for transient failures.
/// </summary>
public sealed class SemanticKernelLlmCompletionService : ILlmCompletionService
{
    private readonly ILogger<SemanticKernelLlmCompletionService> _logger;
    private readonly ResiliencePipeline _pipeline;

    public SemanticKernelLlmCompletionService(ILogger<SemanticKernelLlmCompletionService> logger)
    {
        _logger = logger;
        _pipeline = LlmResilience.CreatePipeline(logger);
    }

    /// <inheritdoc />
    public bool CanUseLiveLlm(AgentWikiConfig config, string? providerOverride = null)
    {
        var provider = NormalizeProvider(providerOverride ?? config.Provider);
        return provider switch
        {
            "azure-openai" => HasAzureCredentials(config),
            "openai" or "github-models" => HasOpenAiCredentials(config),
            "mock" or "offline" => false,
            _ => HasAzureCredentials(config) || HasOpenAiCredentials(config)
        };
    }

    /// <inheritdoc />
    public async Task<LlmCompletionResult> CompleteAsync(
        AgentWikiConfig config,
        string systemPrompt,
        string userPrompt,
        string? modelOverride = null,
        string? providerOverride = null,
        LlmRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

        options ??= LlmRequestOptions.WikiGeneration;
        var provider = NormalizeProvider(providerOverride ?? config.Provider);
        var model = modelOverride
            ?? (provider is "openai" or "github-models"
                ? config.OpenAI.Model ?? config.DefaultModel
                : config.AzureOpenAI.DeploymentName ?? config.DefaultModel);

        _logger.LogInformation("Invoking LLM provider={Provider} model={Model}", provider, model);

        return await _pipeline.ExecuteAsync(async ct =>
        {
            var kernel = BuildKernel(config, provider, model);
            var chat = kernel.GetRequiredService<IChatCompletionService>();

            var history = new ChatHistory();
            history.AddSystemMessage(systemPrompt);
            history.AddUserMessage(userPrompt);

            var settings = CreateExecutionSettings(model, options);

            var message = await chat
                .GetChatMessageContentAsync(history, settings, kernel, ct)
                .ConfigureAwait(false);

            var content = message.Content ?? string.Empty;
            var usage = TryReadUsage(message);

            _logger.LogInformation(
                "LLM completed provider={Provider} model={Model} inputTokens={Input} outputTokens={Output}",
                provider,
                model,
                usage?.InputTokens ?? 0,
                usage?.OutputTokens ?? 0);

            return new LlmCompletionResult
            {
                Content = content,
                TokenUsage = usage,
                Provider = provider,
                Model = model ?? ""
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds prompt settings. Temperature is omitted by default because many current
    /// OpenAI models (reasoning models, some GPT-4.1/5 variants) reject it.
    /// </summary>
    public static OpenAIPromptExecutionSettings CreateExecutionSettings(
        string? model,
        LlmRequestOptions options)
    {
        var settings = new OpenAIPromptExecutionSettings();

        if (options.RequireJsonObject)
        {
            settings.ResponseFormat = "json_object";
        }

        // Only set temperature when explicitly requested AND the model is known to accept it.
        if (options.Temperature is double temperature && SupportsTemperature(model))
        {
            settings.Temperature = temperature;
        }

        return settings;
    }

    /// <summary>
    /// Returns false for model families known to reject the temperature parameter.
    /// </summary>
    public static bool SupportsTemperature(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return true;
        }

        var m = model.Trim().ToLowerInvariant();

        // OpenAI reasoning / o-series
        if (m.StartsWith("o1", StringComparison.Ordinal)
            || m.StartsWith("o3", StringComparison.Ordinal)
            || m.StartsWith("o4", StringComparison.Ordinal)
            || m.Contains("reason", StringComparison.Ordinal))
        {
            return false;
        }

        // Newer families that often reject sampling params
        if (m.StartsWith("gpt-5", StringComparison.Ordinal)
            || m.Contains("codex", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static Kernel BuildKernel(AgentWikiConfig config, string provider, string? model)
    {
        var builder = Kernel.CreateBuilder();

        switch (provider)
        {
            case "openai":
            {
                var apiKey = config.OpenAI.ApiKey
                    ?? throw new InvalidOperationException(
                        "OpenAI ApiKey is not configured. Set openAI.apiKey in .agentwiki/config.json " +
                        "or AGENTWIKI_OpenAI__ApiKey / .env.");
                var modelId = model ?? config.OpenAI.Model ?? config.DefaultModel;
                if (!string.IsNullOrWhiteSpace(config.OpenAI.Endpoint))
                {
                    builder.AddOpenAIChatCompletion(
                        modelId: modelId,
                        apiKey: apiKey,
                        endpoint: new Uri(config.OpenAI.Endpoint));
                }
                else
                {
                    builder.AddOpenAIChatCompletion(modelId: modelId, apiKey: apiKey);
                }

                break;
            }
            case "github-models":
            {
                var apiKey = config.OpenAI.ApiKey
                    ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                    ?? throw new InvalidOperationException(
                        "GitHub Models requires OpenAI:ApiKey, AGENTWIKI_OpenAI__ApiKey, or GITHUB_TOKEN.");
                var modelId = model ?? config.OpenAI.Model ?? config.DefaultModel;
                var endpoint = string.IsNullOrWhiteSpace(config.OpenAI.Endpoint)
                    ? new Uri("https://models.inference.ai.azure.com")
                    : new Uri(config.OpenAI.Endpoint);
                builder.AddOpenAIChatCompletion(modelId: modelId, apiKey: apiKey, endpoint: endpoint);
                break;
            }
            default:
            {
                var endpoint = config.AzureOpenAI.Endpoint
                    ?? throw new InvalidOperationException("AzureOpenAI:Endpoint is not configured.");
                var deployment = model
                    ?? config.AzureOpenAI.DeploymentName
                    ?? config.DefaultModel
                    ?? throw new InvalidOperationException("AzureOpenAI:DeploymentName is not configured.");

                if (config.AzureOpenAI.UseManagedIdentity)
                {
                    builder.AddAzureOpenAIChatCompletion(
                        deploymentName: deployment,
                        endpoint: endpoint,
                        credentials: new DefaultAzureCredential());
                }
                else
                {
                    var apiKey = config.AzureOpenAI.ApiKey
                        ?? throw new InvalidOperationException("AzureOpenAI:ApiKey is not configured.");

                    builder.AddAzureOpenAIChatCompletion(
                        deploymentName: deployment,
                        endpoint: endpoint,
                        apiKey: apiKey);
                }

                break;
            }
        }

        return builder.Build();
    }

    private static bool HasAzureCredentials(AgentWikiConfig config) =>
        !string.IsNullOrWhiteSpace(config.AzureOpenAI.Endpoint)
        && !string.IsNullOrWhiteSpace(config.AzureOpenAI.DeploymentName ?? config.DefaultModel)
        && (!string.IsNullOrWhiteSpace(config.AzureOpenAI.ApiKey) || config.AzureOpenAI.UseManagedIdentity);

    private static bool HasOpenAiCredentials(AgentWikiConfig config) =>
        !string.IsNullOrWhiteSpace(config.OpenAI.ApiKey)
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_TOKEN"));

    private static string NormalizeProvider(string? provider) =>
        (provider ?? "azure-openai").Trim().ToLowerInvariant() switch
        {
            "azure" or "aoai" or "azureopenai" or "azure-openai" => "azure-openai",
            "openai" or "oai" => "openai",
            "github" or "github-models" or "githubmodels" => "github-models",
            "mock" or "none" or "offline" => "offline",
            var p => p
        };

    private static TokenUsage? TryReadUsage(ChatMessageContent message)
    {
        if (message.Metadata is null)
        {
            return null;
        }

        var input = ReadInt(message.Metadata, "PromptTokenCount", "InputTokenCount", "prompt_tokens");
        var output = ReadInt(message.Metadata, "CompletionTokenCount", "OutputTokenCount", "completion_tokens");
        if (input is null && output is null)
        {
            return null;
        }

        return new TokenUsage
        {
            InputTokens = input ?? 0,
            OutputTokens = output ?? 0
        };
    }

    private static int? ReadInt(IReadOnlyDictionary<string, object?> metadata, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!metadata.TryGetValue(key, out var value) || value is null)
            {
                continue;
            }

            switch (value)
            {
                case int i:
                    return i;
                case long l:
                    return (int)l;
                case string s when int.TryParse(s, out var parsed):
                    return parsed;
            }
        }

        return null;
    }
}
