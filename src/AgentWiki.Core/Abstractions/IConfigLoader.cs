using AgentWiki.Core.Models;

namespace AgentWiki.Core.Abstractions;

/// <summary>
/// Loads and merges AgentWiki configuration from all supported sources.
/// Priority (highest wins): CLI overrides → .agentwiki/config.json → env vars → appsettings.
/// </summary>
public interface IConfigLoader
{
    /// <summary>
    /// Loads configuration for the given repository path.
    /// </summary>
    /// <param name="repoPath">Repository root (relative or absolute).</param>
    /// <param name="configFilePath">Optional explicit path to a config JSON file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Merged configuration.</returns>
    Task<AgentWikiConfig> LoadAsync(
        string repoPath,
        string? configFilePath = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies CLI-level overrides onto a loaded config and returns a new instance.
    /// </summary>
    AgentWikiConfig ApplyCliOverrides(
        AgentWikiConfig config,
        string? repoPath = null,
        string? outputPath = null,
        string? model = null,
        string? provider = null);
}
