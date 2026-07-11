using System.Text;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.App.Services;

/// <summary>
/// Writes wiki sections as UTF-8 Markdown files with consistent formatting.
/// Dry-run classifies create / update / unchanged without touching disk.
/// </summary>
public sealed class MarkdownOutputWriter(ILogger<MarkdownOutputWriter> logger) : IOutputWriter
{
    /// <inheritdoc />
    public async Task<OutputWriteResult> WriteAsync(
        string outputPath,
        IReadOnlyList<WikiSection> sections,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(sections);

        var written = new List<string>(sections.Count);
        var wouldCreate = new List<string>();
        var wouldUpdate = new List<string>();
        var unchanged = new List<string>();
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
            var content = NormalizeMarkdown(section.Content);

            if (dryRun)
            {
                if (!File.Exists(absolute))
                {
                    logger.LogInformation("[dry-run] Would create {Path}", relative);
                    wouldCreate.Add(relative);
                }
                else
                {
                    string existing;
                    try
                    {
                        existing = await File.ReadAllTextAsync(absolute, cancellationToken).ConfigureAwait(false);
                        existing = NormalizeMarkdown(existing);
                    }
                    catch
                    {
                        existing = "";
                    }

                    if (string.Equals(existing, content, StringComparison.Ordinal))
                    {
                        logger.LogDebug("[dry-run] Unchanged {Path}", relative);
                        unchanged.Add(relative);
                    }
                    else
                    {
                        logger.LogInformation("[dry-run] Would update {Path}", relative);
                        wouldUpdate.Add(relative);
                    }
                }

                written.Add(relative);
                continue;
            }

            var directory = Path.GetDirectoryName(absolute);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(
                    absolute,
                    content,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken)
                .ConfigureAwait(false);

            logger.LogDebug("Wrote wiki section {Id} → {Path}", section.Id, absolute);
            written.Add(relative);
        }

        if (dryRun)
        {
            logger.LogInformation(
                "[dry-run] Plan: create={Create}, update={Update}, unchanged={Unchanged}",
                wouldCreate.Count,
                wouldUpdate.Count,
                unchanged.Count);
        }

        return new OutputWriteResult
        {
            Files = written,
            WouldCreate = wouldCreate,
            WouldUpdate = wouldUpdate,
            Unchanged = unchanged,
            IsDryRun = dryRun
        };
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
