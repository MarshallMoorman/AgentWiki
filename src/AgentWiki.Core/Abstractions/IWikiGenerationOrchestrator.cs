using AgentWiki.Core.Models;

namespace AgentWiki.Core.Abstractions;

/// <summary>
/// Multi-step wiki generation pipeline (architecture → modules → cross-cutting → index).
/// </summary>
public interface IWikiGenerationOrchestrator
{
    /// <summary>
    /// Runs the full generation pipeline for the given analysis snapshot.
    /// </summary>
    Task<WikiBundle> GenerateAsync(
        RepoAnalysisResult analysis,
        WikiGenerationRequest request,
        CancellationToken cancellationToken = default);
}
