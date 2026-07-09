using AgentWiki.Core.Models;

namespace AgentWiki.Core.Abstractions;

/// <summary>
/// Discovers and classifies repository files for wiki generation.
/// </summary>
public interface IRepoAnalyzer
{
    /// <summary>
    /// Analyzes <paramref name="repoPath"/> using config ignore patterns and limits.
    /// </summary>
    Task<RepoAnalysisResult> AnalyzeAsync(
        string repoPath,
        AgentWikiConfig config,
        CancellationToken cancellationToken = default);
}
