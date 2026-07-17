using AgentWiki.Core.Models;

namespace AgentWiki.Core.Abstractions;

/// <summary>
/// Generates a complete <c>AGENTS.md</c> (full file) from analysis, wiki excerpts, and instruction sources.
/// </summary>
public interface IAgentsMdGenerator
{
    /// <summary>
    /// Builds and optionally writes a full AGENTS.md. Honors dry-run and force.
    /// Migrates/deletes copilot-instructions only after a successful write when configured.
    /// </summary>
    Task<AgentsMdGenerationResult> GenerateAsync(
        AgentsMdGenerationRequest request,
        CancellationToken cancellationToken = default);
}
