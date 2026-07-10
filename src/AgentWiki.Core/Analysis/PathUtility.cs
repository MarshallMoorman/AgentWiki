namespace AgentWiki.Core.Analysis;

/// <summary>
/// Path helpers for CLI input (~ expansion) and portable wiki output (repo-relative paths).
/// </summary>
public static class PathUtility
{
    /// <summary>
    /// Expands a leading <c>~</c> to the current user's home directory, then resolves to a full path.
    /// </summary>
    public static string ExpandAndResolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        return Path.GetFullPath(ExpandHome(path.Trim()));
    }

    /// <summary>
    /// Expands a leading <c>~</c> or <c>~/…</c> to the user profile directory.
    /// Other paths are returned unchanged (still may be relative).
    /// </summary>
    public static string ExpandHome(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        if (path == "~")
        {
            return GetHomeDirectory();
        }

        // ~/… or ~\…
        if (path.Length >= 2 && path[0] == '~' && (path[1] == '/' || path[1] == '\\'))
        {
            return Path.Combine(GetHomeDirectory(), path[2..]);
        }

        return path;
    }

    /// <summary>
    /// Converts an absolute or relative path into a repo-relative path suitable for wiki Markdown.
    /// Never emits machine-specific absolute paths (e.g. <c>/Users/…</c>).
    /// </summary>
    public static string ToRepoRelative(string repoRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var normalized = path.Replace('\\', '/').Trim();
        if (normalized is "." or "./")
        {
            return ".";
        }

        // Already looks relative and not rooted
        if (!Path.IsPathRooted(ExpandHome(normalized))
            && !normalized.StartsWith("~", StringComparison.Ordinal))
        {
            return normalized.TrimStart('/');
        }

        try
        {
            var rootFull = ExpandAndResolve(repoRoot);
            var pathFull = ExpandAndResolve(normalized);
            var relative = Path.GetRelativePath(rootFull, pathFull).Replace('\\', '/');

            // Outside the repo (or same drive edge cases): fall back to file name only.
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                return Path.GetFileName(normalized.TrimEnd('/')) is { Length: > 0 } name
                    ? name
                    : ".";
            }

            return string.IsNullOrEmpty(relative) || relative == "." ? "." : relative;
        }
        catch
        {
            // Best-effort: strip common absolute prefixes by taking the last path segment(s).
            return Path.GetFileName(normalized.TrimEnd('/'));
        }
    }

    /// <summary>
    /// Returns a display string for the repository root in inventory/LLM summaries.
    /// Always portable (never an absolute machine path).
    /// </summary>
    public static string RepoRootDisplayPath => ".";

    private static string GetHomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            return home;
        }

        // Fallbacks for unusual environments
        home = Environment.GetEnvironmentVariable("HOME")
               ?? Environment.GetEnvironmentVariable("USERPROFILE");
        return string.IsNullOrWhiteSpace(home) ? Directory.GetCurrentDirectory() : home;
    }
}
