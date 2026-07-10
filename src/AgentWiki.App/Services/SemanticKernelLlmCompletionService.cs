using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Models;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Polly;

namespace AgentWiki.App.Services;

/// <summary>
/// Semantic Kernel-backed chat completion for Azure OpenAI and OpenAI-compatible endpoints,
/// with Polly retries for transient failures and configurable HTTP timeouts.
/// </summary>
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

        switch (provider)
        {
            case "openai":
            {
                var apiKey = config.OpenAI.ApiKey
                    ?? throw new InvalidOperationException(
                        "OpenAI ApiKey is not configured. Set openAI.apiKey in .agentwiki/config.json " +
                        "or AGENTWIKI_OpenAI__ApiKey / .env.");
                var modelId = model ?? LlmSettings.ResolveModel(config);
                if (!string.IsNullOrWhiteSpace(config.OpenAI.Endpoint))
                {
                    builder.AddOpenAIChatCompletion(
                        modelId: modelId,
                        apiKey: apiKey,
                        endpoint: new Uri(config.OpenAI.Endpoint),
                        httpClient: httpClient);
                }
                else
                {
                    builder.AddOpenAIChatCompletion(
                        modelId: modelId,
                        apiKey: apiKey,
                        httpClient: httpClient);
                }

                break;
            }
            case "github-models":
            {
                var apiKey = config.OpenAI.ApiKey
                    ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                    ?? throw new InvalidOperationException(
                        "GitHub Models requires OpenAI:ApiKey, AGENTWIKI_OpenAI__ApiKey, or GITHUB_TOKEN.");
                var modelId = model ?? LlmSettings.ResolveModel(config);
                var endpoint = string.IsNullOrWhiteSpace(config.OpenAI.Endpoint)
                    ? new Uri("https://models.inference.ai.azure.com")
                    : new Uri(config.OpenAI.Endpoint);
                builder.AddOpenAIChatCompletion(
                    modelId: modelId,
                    apiKey: apiKey,
                    endpoint: endpoint,
                    httpClient: httpClient);
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
                        credentials: new DefaultAzureCredential(),
                        httpClient: httpClient);
                }
                else
                {
                    var apiKey = config.AzureOpenAI.ApiKey
                        ?? throw new InvalidOperationException("AzureOpenAI:ApiKey is not configured.");

                    builder.AddAzureOpenAIChatCompletion(
                        deploymentName: deployment,
                        endpoint: endpoint,
                        apiKey: apiKey,
                        httpClient: httpClient);
                }

                break;
            }
        }

        return builder.Build();
    }

    private HttpClient GetOrCreateHttpClient(int timeoutSeconds)
    {
        lock (_httpLock)
        {
            if (_httpClient is null || _httpTimeoutSeconds != timeoutSeconds)
            {
                _httpClient?.Dispose();
                _httpClient = new HttpClient
                {
                    // Large multi-step wiki prompts need more than the 100s .NET default.
                    Timeout = TimeSpan.FromSeconds(timeoutSeconds)
                };
                _httpTimeoutSeconds = timeoutSeconds;
                _logger.LogDebug("Configured LLM HttpClient timeout={Timeout}s", timeoutSeconds);
            }

            return _httpClient;
        }
    }

    private static int NormalizeTimeoutSeconds(int configured) =>
        configured <= 0 ? 300 : Math.Clamp(configured, 30, 900);

    private static bool HasAzureCredentials(AgentWikiConfig config) =>
        !string.IsNullOrWhiteSpace(config.AzureOpenAI.Endpoint)
        && !string.IsNullOrWhiteSpace(LlmSettings.ResolveModel(config, providerOverride: "azure-openai"))
        && (!string.IsNullOrWhiteSpace(config.AzureOpenAI.ApiKey) || config.AzureOpenAI.UseManagedIdentity);

    private static bool HasOpenAiCredentials(AgentWikiConfig config) =>
        !string.IsNullOrWhiteSpace(config.OpenAI.ApiKey)
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_TOKEN"));

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

    public void Dispose()
    {
        lock (_httpLock)
        {
            _httpClient?.Dispose();
            _httpClient = null;
        }
    }
}
