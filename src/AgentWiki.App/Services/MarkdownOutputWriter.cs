using System.Text;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.App.Services;

/// <summary>
/// Writes wiki sections as UTF-8 Markdown files with consistent formatting.
/// </summary>
public sealed class MarkdownOutputWriter(ILogger<MarkdownOutputWriter> logger) : IOutputWriter
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> WriteAsync(
        string outputPath,
        IReadOnlyList<WikiSection> sections,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(sections);

        var written = new List<string>(sections.Count);
        var absoluteRoot = Path.GetFullPath(outputPath);

        if (!dryRun)
        {
            Directory.CreateDirectory(absoluteRoot);
        }

        foreach (var section in sections)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = section.RelativePath.Replace('\\', '/').TrimStart('/');
            var absolute = Path.Combine(absoluteRoot, relative.Replace('/', Path.DirectorySeparatorChar));

            if (dryRun)
            {
                logger.LogInformation("[dry-run] Would write {Path}", absolute);
                written.Add(relative);
                continue;
            }

            var directory = Path.GetDirectoryName(absolute);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var content = NormalizeMarkdown(section.Content);
            await File.WriteAllTextAsync(absolute, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken)
                .ConfigureAwait(false);

            logger.LogDebug("Wrote wiki section {Id} → {Path}", section.Id, absolute);
            written.Add(relative);
        }

        return written;
    }

    private static string NormalizeMarkdown(string content)
    {
        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        if (!normalized.EndsWith('\n'))
        {
            normalized += "\n";
        }

        return normalized;
    }
}
