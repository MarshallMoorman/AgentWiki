using AgentWiki.Core.Models;

namespace AgentWiki.Core.Abstractions;

/// <summary>
/// Writes wiki sections as Markdown files to disk.
/// </summary>
public interface IOutputWriter
{
    /// <summary>
    /// Writes the provided sections under <paramref name="outputPath"/>.
    /// When <paramref name="dryRun"/> is true, classifies create/update/unchanged without writing.
    /// </summary>
    Task<OutputWriteResult> WriteAsync(
        string outputPath,
        IReadOnlyList<WikiSection> sections,
        bool dryRun = false,
        CancellationToken cancellationToken = default);
}
