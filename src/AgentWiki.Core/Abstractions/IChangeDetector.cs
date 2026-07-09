using AgentWiki.Core.Models;

namespace AgentWiki.Core.Abstractions;

/// <summary>
/// Detects repository changes since the last successful wiki generation.
/// </summary>
public interface IChangeDetector
{
    /// <summary>
    /// Compares the current repository state to <c>.agentwiki/last-run.json</c>
    /// and maps changed files onto wiki sections/modules.
    /// </summary>
    Task<ChangeDetectionResult> DetectAsync(
        string repoPath,
        AgentWikiConfig config,
        RepoAnalysisResult? analysis = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Loads and persists last-run metadata for incremental updates.
/// </summary>
public interface ILastRunStore
{
    Task<LastRunState?> LoadAsync(string repoPath, CancellationToken cancellationToken = default);

    Task SaveAsync(string repoPath, LastRunState state, CancellationToken cancellationToken = default);
}
