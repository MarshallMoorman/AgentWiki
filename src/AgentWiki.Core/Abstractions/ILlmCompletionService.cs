using AgentWiki.Core.Models;

namespace AgentWiki.Core.Abstractions;

/// <summary>
/// Thin chat-completion abstraction over Semantic Kernel (or test doubles).
/// </summary>
public interface ILlmCompletionService
{
    /// <summary>
    /// Returns true when credentials/config allow a live LLM call for the request.
    /// </summary>
    bool CanUseLiveLlm(AgentWikiConfig config, string? providerOverride = null);

    /// <summary>
    /// Completes a chat prompt and returns raw text plus optional token usage.
    /// </summary>
    Task<LlmCompletionResult> CompleteAsync(
        AgentWikiConfig config,
        string systemPrompt,
        string userPrompt,
        string? modelOverride = null,
        string? providerOverride = null,
        LlmRequestOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Optional knobs for a single completion request.</summary>
public sealed class LlmRequestOptions
{
    /// <summary>
    /// When true (default for wiki generation), request JSON object response format.
    /// Disable for connectivity probes and free-form answers.
    /// </summary>
    public bool RequireJsonObject { get; init; } = true;

    /// <summary>
    /// Optional temperature. Leave null to omit (recommended for models that reject temperature).
    /// </summary>
    public double? Temperature { get; init; }

    /// <summary>Default options for multi-step wiki generation (JSON, no temperature).</summary>
    public static LlmRequestOptions WikiGeneration { get; } = new()
    {
        RequireJsonObject = true,
        Temperature = null
    };

    /// <summary>Default options for provider connectivity tests.</summary>
    public static LlmRequestOptions ConnectivityProbe { get; } = new()
    {
        RequireJsonObject = false,
        Temperature = null
    };
}

/// <summary>Result of a single LLM completion.</summary>
public sealed class LlmCompletionResult
{
    public required string Content { get; init; }
    public TokenUsage? TokenUsage { get; init; }
    public string Provider { get; init; } = "";
    public string Model { get; init; } = "";
}
