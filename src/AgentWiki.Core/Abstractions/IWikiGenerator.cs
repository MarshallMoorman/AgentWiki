using AgentWiki.Core.Models;

namespace AgentWiki.Core.Abstractions;

/// <summary>
/// Orchestrates wiki content generation (full or incremental).
/// Phase 1 provides a placeholder implementation that writes a hello-world wiki.
/// </summary>
public interface IWikiGenerator
{
    /// <summary>
    /// Generates (or updates) the agent-optimized wiki for a repository.
    /// </summary>
    Task<GenerationResult> GenerateAsync(
        WikiGenerationRequest request,
        CancellationToken cancellationToken = default);
}
