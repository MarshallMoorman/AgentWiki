using AgentWiki.Core.Analysis;
using AgentWiki.Core.Models;

namespace AgentWiki.Desktop.Services;

/// <summary>Shared path resolution for desktop view-models (matches CLI commands).</summary>
public static class PathResolver
{
    public static string ResolveRepo(string? repoPath) =>
        PathUtility.ExpandAndResolve(string.IsNullOrWhiteSpace(repoPath) ? "." : repoPath);

    public static string ResolveOutput(AgentWikiConfig config, string repoPath)
    {
        var output = config.OutputPath;
        return Path.IsPathRooted(PathUtility.ExpandHome(output))
            ? PathUtility.ExpandAndResolve(output)
            : PathUtility.ExpandAndResolve(Path.Combine(repoPath, output));
    }

    public static string DisplayHomeRelative(string absolutePath)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (absolutePath.StartsWith(home, StringComparison.OrdinalIgnoreCase))
        {
            return "~" + absolutePath[home.Length..].Replace('\\', '/');
        }

        return absolutePath.Replace('\\', '/');
    }
}
