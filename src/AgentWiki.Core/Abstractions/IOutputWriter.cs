using AgentWiki.Core.Models;

namespace AgentWiki.Core.Abstractions;

/// <summary>
/// Writes wiki sections as Markdown files to disk.
/// </summary>
public interface IOutputWriter
{
    /// <summary>
    /// Writes the provided sections under <paramref name="outputPath"/>.
    /// </summary>
    /// <returns>Relative paths of files written.</returns>
    Task<IReadOnlyList<string>> WriteAsync(
        string outputPath,
        IReadOnlyList<WikiSection> sections,
        bool dryRun = false,
        CancellationToken cancellationToken = default);
}
