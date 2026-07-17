using AgentWiki.Core;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.App.Services;

/// <summary>Discovers well-known agent instruction files (Copilot, etc.) for migration into AGENTS.md.</summary>
public static class InstructionFileDiscovery
{
    /// <summary>
    /// Returns instruction sources. Primary Copilot paths are marked for deletion after migration.
    /// </summary>
    public static async Task<IReadOnlyList<InstructionSource>> DiscoverAsync(
        string repoPath,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(repoPath);
        var results = new List<InstructionSource>();

        await TryAddAsync(
                root,
                Constants.Paths.CopilotInstructionsGithub,
                deleteAfter: true,
                results,
                logger,
                cancellationToken)
            .ConfigureAwait(false);

        await TryAddAsync(
                root,
                Constants.Paths.CopilotInstructionsRoot,
                deleteAfter: true,
                results,
                logger,
                cancellationToken)
            .ConfigureAwait(false);

        // Path-specific .github/instructions/*.instructions.md — merge but do not auto-delete.
        var instructionsDir = Path.Combine(root, ".github", "instructions");
        if (Directory.Exists(instructionsDir))
        {
            foreach (var file in Directory.EnumerateFiles(instructionsDir, "*.instructions.md")
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                         .Take(10))
            {
                var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                try
                {
                    var content = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        continue;
                    }

                    results.Add(new InstructionSource
                    {
                        RelativePath = rel,
                        AbsolutePath = file,
                        Content = content,
                        DeleteAfterMigration = false
                    });
                    logger?.LogDebug("Discovered path-specific instructions at {Path}", rel);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Could not read instruction file {Path}", file);
                }
            }
        }

        return results;
    }

    private static async Task TryAddAsync(
        string root,
        string relativePath,
        bool deleteAfter,
        List<InstructionSource> results,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var absolute = Path.Combine(root, relativePath);
        if (!File.Exists(absolute))
        {
            return;
        }

        try
        {
            var content = await File.ReadAllTextAsync(absolute, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            results.Add(new InstructionSource
            {
                RelativePath = relativePath.Replace('\\', '/'),
                AbsolutePath = absolute,
                Content = content,
                DeleteAfterMigration = deleteAfter
            });
            logger?.LogInformation("Discovered instruction file {Path}", relativePath);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not read instruction file {Path}", absolute);
        }
    }
}
