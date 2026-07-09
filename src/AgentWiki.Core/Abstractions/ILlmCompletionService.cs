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
        CancellationToken cancellationToken = default);
}

/// <summary>Result of a single LLM completion.</summary>
public sealed class LlmCompletionResult
{
    public required string Content { get; init; }
    public TokenUsage? TokenUsage { get; init; }
    public string Provider { get; init; } = "";
    public string Model { get; init; } = "";
}
