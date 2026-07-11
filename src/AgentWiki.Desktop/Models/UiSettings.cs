namespace AgentWiki.Desktop.Models;

/// <summary>
/// Desktop-only preferences stored at <c>~/.agentwiki/ui-settings.json</c>.
/// Not part of the shared CLI config model.
/// </summary>
public sealed class UiSettings
{
    public const int MaxRecentRepos = 10;

    /// <summary>Most recently used repository paths (newest first).</summary>
    public List<string> RecentRepos { get; set; } = [];

    /// <summary>Last selected repository path.</summary>
    public string? LastRepoPath { get; set; }

    /// <summary>
    /// Appearance preference: <c>system</c> (default, follow OS), <c>dark</c>, or <c>light</c>.
    /// Stored in <c>~/.agentwiki/ui-settings.json</c>.
    /// </summary>
    public string Theme { get; set; } = "system";

    public void RememberRepo(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return;
        }

        var normalized = Path.GetFullPath(absolutePath);
        RecentRepos.RemoveAll(p =>
            string.Equals(Path.GetFullPath(p), normalized, StringComparison.OrdinalIgnoreCase));
        RecentRepos.Insert(0, normalized);
        while (RecentRepos.Count > MaxRecentRepos)
        {
            RecentRepos.RemoveAt(RecentRepos.Count - 1);
        }

        LastRepoPath = normalized;
    }

    public void ClearRecent()
    {
        RecentRepos.Clear();
        LastRepoPath = null;
    }
}
