using System.ClientModel;
using System.ClientModel.Primitives;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Models;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI;
using Polly;
using AgentWiki.Core;

namespace AgentWiki.App.Services;

/// <summary>
/// Semantic Kernel-backed chat completion for Azure OpenAI and OpenAI-compatible endpoints,
/// with Polly retries for transient failures and configurable HTTP timeouts.
/// </summary>
/// <remarks>
/// Important: the OpenAI/.NET ClientPipeline default <c>NetworkTimeout</c> is <b>100 seconds</b>.
/// Setting only <see cref="HttpClient.Timeout"/> is not enough — long completions still fail around
/// ~5 minutes when the SDK retries 100s network operations. We set <c>NetworkTimeout</c> from
/// <see cref="AgentWikiConfig.LlmTimeoutSeconds"/> and disable the SDK retry policy (Polly retries instead).
/// </remarks>
public sealed class SemanticKernelLlmCompletionService : ILlmCompletionService, IDisposable
{
    private readonly ILogger<SemanticKernelLlmCompletionService> _logger;
    private readonly ResiliencePipeline _pipeline;
    private readonly object _httpLock = new();
    private HttpClient? _httpClient;
    private int _httpTimeoutSeconds;

    public SemanticKernelLlmCompletionService(ILogger<SemanticKernelLlmCompletionService> logger)
    {
        _logger = logger;
        _pipeline = LlmResilience.CreatePipeline(logger);
    }

    /// <inheritdoc />
    public bool CanUseLiveLlm(AgentWikiConfig config, string? providerOverride = null)
    {
        var provider = LlmSettings.NormalizeProvider(providerOverride ?? config.Provider);
        return provider switch
        {
            Constants.Providers.AzureOpenAi => HasAzureCredentials(config),
            Constants.Providers.OpenAi or Constants.Providers.GitHubModels => HasOpenAiCredentials(config),
            Constants.Providers.Mock or Constants.Providers.Offline => false,
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
        var provider = LlmSettings.NormalizeProvider(providerOverride ?? config.Provider);
        var model = LlmSettings.ResolveModel(config, modelOverride, providerOverride);

        // OpenAI rejects response_format=json_object unless some message contains the word "json".
        if (options.RequireJsonObject)
        {
            (systemPrompt, userPrompt) = EnsureJsonMentionInMessages(systemPrompt, userPrompt);
        }

        var timeoutSeconds = NormalizeTimeoutSeconds(config.LlmTimeoutSeconds);
        _logger.LogInformation(
            "Invoking LLM provider={Provider} model={Model} timeout={Timeout}s promptChars={Chars} requireJson={RequireJson}",
            provider,
            model,
            timeoutSeconds,
            systemPrompt.Length + userPrompt.Length,
            options.RequireJsonObject);

        try
        {
            return await _pipeline.ExecuteAsync(async ct =>
            {
                var kernel = BuildKernel(config, provider, model, timeoutSeconds);
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
                    "LLM completed provider={Provider} model={Model} inputTokens={Input} outputTokens={Output} contentChars={Chars}",
                    provider,
                    model,
                    usage?.InputTokens ?? 0,
                    usage?.OutputTokens ?? 0,
                    content.Length);

                if (string.IsNullOrWhiteSpace(content))
                {
                    _logger.LogWarning(
                        "LLM returned empty content (provider={Provider} model={Model}). Check model compatibility and prompt.",
                        provider,
                        model);
                }

                return new LlmCompletionResult
                {
                    Content = content,
                    TokenUsage = usage,
                    Provider = provider,
                    Model = model ?? ""
                };
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (LlmResilience.IsTimeoutFailure(ex, cancellationToken))
        {
            throw new TimeoutException(
                $"LLM request timed out after {timeoutSeconds}s (provider={provider}, model={model}). " +
                "Try a faster model, raise llmTimeoutSeconds in config, or reduce maxLlmSummaryChars / maxFilesToAnalyze.",
                ex);
        }
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
    /// OpenAI API rule: when using response_format=json_object, at least one message
    /// must contain the substring "json" (case-insensitive).
    /// </summary>
    public static (string System, string User) EnsureJsonMentionInMessages(string systemPrompt, string userPrompt)
    {
        const string marker =
            "\n\nRespond with a single valid JSON object only (no markdown fences, no commentary).";

        var hasJson =
            systemPrompt.Contains("json", StringComparison.OrdinalIgnoreCase)
            || userPrompt.Contains("json", StringComparison.OrdinalIgnoreCase);

        if (hasJson)
        {
            return (systemPrompt, userPrompt);
        }

        // Prefer appending to the user message so task prompts stay primary.
        return (systemPrompt, userPrompt.TrimEnd() + marker);
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

        if (m.StartsWith("o1", StringComparison.Ordinal)
            || m.StartsWith("o3", StringComparison.Ordinal)
            || m.StartsWith("o4", StringComparison.Ordinal)
            || m.Contains("reason", StringComparison.Ordinal))
        {
            return false;
        }

        if (m.StartsWith("gpt-5", StringComparison.Ordinal)
            || m.Contains("codex", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private Kernel BuildKernel(AgentWikiConfig config, string provider, string? model, int timeoutSeconds)
    {
        var builder = Kernel.CreateBuilder();
        var httpClient = GetOrCreateHttpClient(timeoutSeconds);

        // System.ClientModel default NetworkTimeout is 100s. With the OpenAI SDK's built-in
        // ClientRetryPolicy (≈3 tries) that surfaces as ~5 minute failures even when
        // HttpClient.Timeout / AGENTWIKI_LlmTimeoutSeconds (default 1200s).
        _logger.LogInformation(
            "LLM client timeouts: networkTimeout={NetworkTimeout}s httpClientTimeout={HttpTimeout}s (SDK default network timeout is 100s)",
            timeoutSeconds,
            timeoutSeconds);

        switch (provider)
        {
            case Constants.Providers.OpenAi:
            {
                var apiKey = config.OpenAI.ApiKey
                    ?? throw new InvalidOperationException(
                        "OpenAI ApiKey is not configured. Set openAI.apiKey in .agentwiki/config.json " +
                        "or AGENTWIKI_OpenAI__ApiKey / AGENTWIKI_ApiKey / OPENAI_API_KEY / .env.");
                var modelId = model ?? LlmSettings.ResolveModel(config);
                Uri? endpoint = string.IsNullOrWhiteSpace(config.OpenAI.Endpoint)
                    ? null
                    : new Uri(config.OpenAI.Endpoint);
                var client = CreateOpenAiClient(apiKey, endpoint, httpClient, timeoutSeconds);
                builder.AddOpenAIChatCompletion(modelId: modelId, openAIClient: client);
                break;
            }
            case Constants.Providers.GitHubModels:
            {
                var apiKey = config.OpenAI.ApiKey
                    ?? Environment.GetEnvironmentVariable(Constants.Env.GitHubToken)
                    ?? throw new InvalidOperationException(
                        "GitHub Models requires OpenAI:ApiKey, AGENTWIKI_OpenAI__ApiKey, or GITHUB_TOKEN.");
                var modelId = model ?? LlmSettings.ResolveModel(config);
                var endpoint = string.IsNullOrWhiteSpace(config.OpenAI.Endpoint)
                    ? new Uri("https://models.inference.ai.azure.com")
                    : new Uri(config.OpenAI.Endpoint);
                var client = CreateOpenAiClient(apiKey, endpoint, httpClient, timeoutSeconds);
                builder.AddOpenAIChatCompletion(modelId: modelId, openAIClient: client);
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

                var azureClient = CreateAzureOpenAiClient(config, endpoint, httpClient, timeoutSeconds);
                builder.AddAzureOpenAIChatCompletion(
                    deploymentName: deployment,
                    azureOpenAIClient: azureClient);
                break;
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// Builds an <see cref="OpenAIClient"/> with NetworkTimeout aligned to AgentWiki config.
    /// </summary>
    public static OpenAIClient CreateOpenAiClient(
        string apiKey,
        Uri? endpoint,
        HttpClient httpClient,
        int timeoutSeconds)
    {
        var options = new OpenAIClientOptions
        {
            NetworkTimeout = TimeSpan.FromSeconds(timeoutSeconds),
            // Polly owns retries; SDK retries × NetworkTimeout was masking as a 5‑minute failure.
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            Transport = new HttpClientPipelineTransport(httpClient)
        };
        if (endpoint is not null)
        {
            options.Endpoint = endpoint;
        }

        return new OpenAIClient(new ApiKeyCredential(apiKey), options);
    }

    /// <summary>
    /// Builds an <see cref="AzureOpenAIClient"/> with NetworkTimeout aligned to AgentWiki config.
    /// </summary>
    public static AzureOpenAIClient CreateAzureOpenAiClient(
        AgentWikiConfig config,
        string endpoint,
        HttpClient httpClient,
        int timeoutSeconds)
    {
        var options = new AzureOpenAIClientOptions
        {
            NetworkTimeout = TimeSpan.FromSeconds(timeoutSeconds),
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            Transport = new HttpClientPipelineTransport(httpClient)
        };

        var uri = new Uri(endpoint);
        if (config.AzureOpenAI.UseManagedIdentity)
        {
            return new AzureOpenAIClient(uri, new DefaultAzureCredential(), options);
        }

        var apiKey = config.AzureOpenAI.ApiKey
            ?? throw new InvalidOperationException("AzureOpenAI:ApiKey is not configured.");
        return new AzureOpenAIClient(uri, new ApiKeyCredential(apiKey), options);
    }

    private HttpClient GetOrCreateHttpClient(int timeoutSeconds)
    {
        lock (_httpLock)
        {
            if (_httpClient is null || _httpTimeoutSeconds != timeoutSeconds)
            {
                _httpClient?.Dispose();
                // HttpClient.Timeout is a whole-request ceiling; keep it slightly above NetworkTimeout
                // so the ClientPipeline network timeout is the controlling budget.
                _httpClient = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(Math.Min(
                        timeoutSeconds + Constants.Llm.HttpClientTimeoutSlackSeconds,
                        Constants.Llm.AbsoluteMaxHttpClientTimeoutSeconds))
                };
                _httpTimeoutSeconds = timeoutSeconds;
                _logger.LogDebug(
                    "Configured LLM HttpClient timeout={Timeout}s (networkTimeout={Network}s)",
                    _httpClient.Timeout.TotalSeconds,
                    timeoutSeconds);
            }

            return _httpClient;
        }
    }

    private static int NormalizeTimeoutSeconds(int configured) =>
        configured <= 0
            ? Constants.Config.LlmTimeoutSeconds
            : Math.Clamp(configured, Constants.Llm.MinTimeoutSeconds, Constants.Llm.MaxTimeoutSeconds);

    private static bool HasAzureCredentials(AgentWikiConfig config) =>
        !string.IsNullOrWhiteSpace(config.AzureOpenAI.Endpoint)
        && !string.IsNullOrWhiteSpace(
            LlmSettings.ResolveModel(config, providerOverride: Constants.Providers.AzureOpenAi))
        && (!string.IsNullOrWhiteSpace(config.AzureOpenAI.ApiKey) || config.AzureOpenAI.UseManagedIdentity);

    private static bool HasOpenAiCredentials(AgentWikiConfig config) =>
        !string.IsNullOrWhiteSpace(config.OpenAI.ApiKey)
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Constants.Env.GitHubToken));

    /// <summary>
    /// Reads token usage from a chat completion result.
    /// Current OpenAI / Azure connectors put counts on <c>ChatMessageContent.InnerContent</c>
    /// (<c>OpenAI.Chat.ChatCompletion.Usage</c>) or under metadata key <c>Usage</c> as
    /// <c>ChatTokenUsage</c> — not as flat integer metadata keys.
    /// </summary>
    /// <remarks>Public for unit tests.</remarks>
    public static TokenUsage? TryReadUsage(ChatMessageContent message)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Preferred: OpenAI SDK ChatCompletion on InnerContent (SK 1.x + OpenAI 2.x).
        if (message.InnerContent is OpenAI.Chat.ChatCompletion chatCompletion
            && chatCompletion.Usage is { } chatUsage)
        {
            return new TokenUsage
            {
                InputTokens = chatUsage.InputTokenCount,
                OutputTokens = chatUsage.OutputTokenCount
            };
        }

        if (message.Metadata is { } metadata)
        {
            // Metadata["Usage"] — ChatTokenUsage (OpenAI) or CompletionsUsage-like shapes.
            if (TryGetMetadataValue(metadata, "Usage", out var usageObj)
                && usageObj is not null
                && TryReadUsageFromObject(usageObj) is { } fromUsage)
            {
                return fromUsage;
            }

            // Legacy / alternate flat metadata keys (older SK connectors).
            var input = ReadInt(
                metadata,
                "PromptTokenCount",
                "InputTokenCount",
                "InputTokens",
                "prompt_tokens");
            var output = ReadInt(
                metadata,
                "CompletionTokenCount",
                "OutputTokenCount",
                "OutputTokens",
                "completion_tokens");
            if (input is not null || output is not null)
            {
                return new TokenUsage
                {
                    InputTokens = input ?? 0,
                    OutputTokens = output ?? 0
                };
            }
        }

        // Last resort: any object-shaped InnerContent with usage-like properties.
        if (message.InnerContent is not null
            && TryReadUsageFromObject(message.InnerContent) is { } fromInner)
        {
            return fromInner;
        }

        return null;
    }

    private static TokenUsage? TryReadUsageFromObject(object usageObj)
    {
        // OpenAI.Chat.ChatTokenUsage
        if (usageObj is OpenAI.Chat.ChatTokenUsage openAiUsage)
        {
            return new TokenUsage
            {
                InputTokens = openAiUsage.InputTokenCount,
                OutputTokens = openAiUsage.OutputTokenCount
            };
        }

        // Nested .Usage property (e.g. ChatCompletion-like without direct type match).
        var type = usageObj.GetType();
        var nestedUsage = type.GetProperty("Usage")?.GetValue(usageObj);
        if (nestedUsage is not null && !ReferenceEquals(nestedUsage, usageObj))
        {
            var nested = TryReadUsageFromObject(nestedUsage);
            if (nested is not null)
            {
                return nested;
            }
        }

        // Reflection for CompletionsUsage (PromptTokens/CompletionTokens) and similar DTOs.
        var input = ReadPropertyInt(
            usageObj,
            "InputTokenCount",
            "InputTokens",
            "PromptTokens",
            "PromptTokenCount");
        var output = ReadPropertyInt(
            usageObj,
            "OutputTokenCount",
            "OutputTokens",
            "CompletionTokens",
            "CompletionTokenCount");

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

    private static int? ReadPropertyInt(object target, params string[] propertyNames)
    {
        var type = target.GetType();
        foreach (var name in propertyNames)
        {
            var prop = type.GetProperty(name);
            if (prop is null)
            {
                continue;
            }

            var value = prop.GetValue(target);
            var parsed = CoerceToInt(value);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        return null;
    }

    private static bool TryGetMetadataValue(
        IReadOnlyDictionary<string, object?> metadata,
        string key,
        out object? value)
    {
        foreach (var pair in metadata)
        {
            if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static int? ReadInt(IReadOnlyDictionary<string, object?> metadata, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryGetMetadataValue(metadata, key, out var value) || value is null)
            {
                continue;
            }

            var parsed = CoerceToInt(value);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        return null;
    }

    private static int? CoerceToInt(object? value) =>
        value switch
        {
            null => null,
            int i => i,
            long l => (int)l,
            uint u => (int)u,
            ulong ul => (int)ul,
            short s => s,
            ushort us => us,
            float f => (int)f,
            double d => (int)d,
            decimal m => (int)m,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => null
        };

    public void Dispose()
    {
        lock (_httpLock)
        {
            _httpClient?.Dispose();
            _httpClient = null;
        }
    }
}
