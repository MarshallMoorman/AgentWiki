using AgentWiki.Core.Models;

namespace AgentWiki.Core.Abstractions;

/// <summary>
/// Produces a structured architecture document for a repository (LLM or offline).
/// </summary>
public interface IArchitectureGenerator
{
    /// <summary>
    /// Generates architecture content from the analysis inventory and config.
    /// </summary>
    Task<ArchitectureDocument> GenerateAsync(
        RepoAnalysisResult analysis,
        AgentWikiConfig config,
        string? modelOverride = null,
        string? providerOverride = null,
        CancellationToken cancellationToken = default);
}
