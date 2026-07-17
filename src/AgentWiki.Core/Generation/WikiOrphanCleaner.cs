namespace AgentWiki.Core.Generation;

/// <summary>
/// Clears or prunes wiki subtrees so full regenerates do not accumulate stale module pages
/// when the LLM invents new ids between runs.
/// </summary>
public static class WikiOrphanCleaner
{
    /// <summary>
    /// Deletes all markdown under <c>modules/</c> and <c>cross-cutting/</c>.
    /// Call before a full generate so the write set is the sole source of truth.
    /// Top-level fixed pages (index.md, architecture.md, …) are left in place and overwritten by name.
    /// </summary>
    public static IReadOnlyList<string> ClearModuleAreas(string wikiOutputPath, bool dryRun = false)
    {
        if (string.IsNullOrWhiteSpace(wikiOutputPath) || !Directory.Exists(wikiOutputPath))
        {
            return [];
        }

        var deleted = new List<string>();
        foreach (var folder in new[] { "modules", "cross-cutting" })
        {
            var dir = Path.Combine(wikiOutputPath, folder);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.md"))
            {
                var rel = $"{folder}/{Path.GetFileName(file)}".Replace('\\', '/');
                if (dryRun)
                {
                    deleted.Add(rel);
                    continue;
                }

                try
                {
                    File.Delete(file);
                    deleted.Add(rel);
                }
                catch
                {
                    // best-effort
                }
            }
        }

        return deleted;
    }

    /// <summary>
    /// Deletes <c>modules/*.md</c> and <c>cross-cutting/*.md</c> that are not in
    /// <paramref name="plannedRelativePaths"/>. Does not touch top-level fixed pages
    /// (index, architecture, …) unless they are absent from the plan and
    /// <paramref name="removeUnplannedTopLevel"/> is true.
    /// </summary>
    /// <returns>Relative paths that were deleted (or would be, when dryRun).</returns>
    public static IReadOnlyList<string> RemoveOrphans(
        string wikiOutputPath,
        IReadOnlyCollection<string> plannedRelativePaths,
        bool dryRun = false,
        bool removeUnplannedTopLevel = false)
    {
        if (string.IsNullOrWhiteSpace(wikiOutputPath) || !Directory.Exists(wikiOutputPath))
        {
            return [];
        }

        var planned = plannedRelativePaths
            .Select(p => p.Replace('\\', '/').TrimStart('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var deleted = new List<string>();
        CollectDirectoryOrphans(
            wikiOutputPath,
            "modules",
            planned,
            dryRun,
            deleted);
        CollectDirectoryOrphans(
            wikiOutputPath,
            "cross-cutting",
            planned,
            dryRun,
            deleted);

        if (removeUnplannedTopLevel)
        {
            foreach (var file in Directory.EnumerateFiles(wikiOutputPath, "*.md"))
            {
                var rel = Path.GetFileName(file);
                if (planned.Contains(rel))
                {
                    continue;
                }

                // Keep meta sidecar if present as md (unlikely)
                if (rel.StartsWith(".", StringComparison.Ordinal))
                {
                    continue;
                }

                if (dryRun)
                {
                    deleted.Add(rel);
                    continue;
                }

                try
                {
                    File.Delete(file);
                    deleted.Add(rel);
                }
                catch
                {
                    // best-effort
                }
            }
        }

        return deleted;
    }

    private static void CollectDirectoryOrphans(
        string wikiRoot,
        string folderName,
        HashSet<string> planned,
        bool dryRun,
        List<string> deleted)
    {
        var dir = Path.Combine(wikiRoot, folderName);
        if (!Directory.Exists(dir))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(dir, "*.md"))
        {
            var rel = $"{folderName}/{Path.GetFileName(file)}".Replace('\\', '/');
            if (planned.Contains(rel))
            {
                continue;
            }

            if (dryRun)
            {
                deleted.Add(rel);
                continue;
            }

            try
            {
                File.Delete(file);
                deleted.Add(rel);
            }
            catch
            {
                // best-effort
            }
        }
    }
}
