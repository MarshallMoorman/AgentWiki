using AgentWiki.Core.Models;

namespace AgentWiki.Core.Abstractions;

/// <summary>
/// Multi-step wiki generation pipeline (architecture → modules → cross-cutting → index).
/// </summary>
public interface IWikiGenerationOrchestrator
{
    /// <summary>
    /// Runs the generation pipeline for the given analysis snapshot.
    /// When <paramref name="scope"/> is selective, only in-scope sections use live LLM generation;
    /// out-of-scope sections use fast offline content so the wiki stays coherent.
    /// </summary>
    Task<WikiBundle> GenerateAsync(
        RepoAnalysisResult analysis,
        WikiGenerationRequest request,
        IncrementalScope? scope = null,
        CancellationToken cancellationToken = default);
}
