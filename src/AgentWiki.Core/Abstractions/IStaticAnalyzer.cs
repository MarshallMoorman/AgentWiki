using AgentWiki.Core.Models;

namespace AgentWiki.Core.Abstractions;

/// <summary>
/// Optional static analysis over repository source (Roslyn syntax walk for C#).
/// Must be offline-safe and fail gracefully for non-.NET repos.
/// </summary>
public interface IStaticAnalyzer
{
    /// <summary>
    /// Analyzes selected source files from an inventory result.
    /// Returns a skipped/empty result when disabled, non-.NET, or on failure.
    /// </summary>
    Task<StaticAnalysisResult> AnalyzeAsync(
        RepoAnalysisResult analysis,
        AgentWikiConfig config,
        CancellationToken cancellationToken = default);
}
