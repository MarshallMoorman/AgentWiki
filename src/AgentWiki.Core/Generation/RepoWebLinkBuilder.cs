using System.Text.RegularExpressions;

namespace AgentWiki.Core.Generation;

/// <summary>Resolved web browsing links for a repository and optional path.</summary>
public sealed class RepoWebLinks
{
    public string? RepoUrl { get; init; }
    public string? Branch { get; init; }
    public string? RemoteName { get; init; }
    public string? Hosting { get; init; } // github | azure-devops | unknown
    public string? WikiIndexWebUrl { get; init; }
    public string? FileWebUrlTemplate { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Build a web URL for a repo-relative file path (uses current branch).</summary>
    public string? FileUrl(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(RepoUrl) || string.IsNullOrWhiteSpace(Branch))
        {
            return null;
        }

        var rel = relativePath.Replace('\\', '/').TrimStart('/');
        return Hosting switch
        {
            "github" => $"{RepoUrl.TrimEnd('/')}/blob/{Branch}/{rel}",
            "azure-devops" => BuildAdoBrowseUrl(RepoUrl, Branch, rel),
            _ => null
        };
    }

    private static string BuildAdoBrowseUrl(string repoUrl, string branch, string relativePath)
    {
        // https://dev.azure.com/{org}/{project}/_git/{repo}?path=/{file}&version=GBmain&_a=contents
        var baseUrl = repoUrl.TrimEnd('/');
        var path = "/" + relativePath.TrimStart('/');
        return $"{baseUrl}?path={Uri.EscapeDataString(path)}&version=GB{Uri.EscapeDataString(branch)}&_a=contents";
    }
}

/// <summary>
/// Builds GitHub and Azure DevOps web URLs from git remote + branch.
/// Prefer tracked upstream remote for the current branch, else origin, else first remote.
/// </summary>
public static class RepoWebLinkBuilder
{
    /// <summary>
    /// Build links from already-resolved remote URL and branch (unit-test friendly).
    /// </summary>
    public static RepoWebLinks Build(
        string? remoteUrl,
        string? branch,
        string? wikiRelativePath = null,
        string? remoteName = null)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            warnings.Add("No git remote URL available; falling back to local paths.");
            return new RepoWebLinks { Warnings = warnings, Branch = branch, RemoteName = remoteName };
        }

        var normalized = NormalizeRemoteToHttps(remoteUrl.Trim());
        if (normalized is null)
        {
            warnings.Add($"Could not normalize remote URL '{remoteUrl}' to a web base.");
            return new RepoWebLinks
            {
                Warnings = warnings,
                Branch = branch,
                RemoteName = remoteName
            };
        }

        var hosting = DetectHosting(normalized);
        var branchName = string.IsNullOrWhiteSpace(branch) ? "main" : branch.Trim();
        if (string.IsNullOrWhiteSpace(branch))
        {
            warnings.Add("Branch unknown; defaulting web links to 'main'.");
        }

        var links = new RepoWebLinks
        {
            RepoUrl = normalized,
            Branch = branchName,
            RemoteName = remoteName,
            Hosting = hosting,
            Warnings = warnings
        };

        if (!string.IsNullOrWhiteSpace(wikiRelativePath))
        {
            var wiki = wikiRelativePath.Replace('\\', '/').TrimEnd('/') + "/index.md";
            // Use object initializer pattern via local
            return new RepoWebLinks
            {
                RepoUrl = links.RepoUrl,
                Branch = links.Branch,
                RemoteName = links.RemoteName,
                Hosting = links.Hosting,
                WikiIndexWebUrl = links.FileUrl(wiki),
                Warnings = warnings
            };
        }

        return links;
    }

    /// <summary>Normalize git remote to HTTPS browse base (no .git suffix).</summary>
    public static string? NormalizeRemoteToHttps(string remote)
    {
        remote = remote.Trim();

        // git@github.com:org/repo.git
        var sshMatch = Regex.Match(remote, @"^git@([^:]+):(.+)$", RegexOptions.IgnoreCase);
        if (sshMatch.Success)
        {
            var host = sshMatch.Groups[1].Value;
            var path = sshMatch.Groups[2].Value.TrimEnd('/');
            if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^4];
            }

            return $"https://{host}/{path}";
        }

        // ssh://git@github.com/org/repo.git
        if (remote.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(remote);
                var path = uri.AbsolutePath.TrimStart('/');
                if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                {
                    path = path[..^4];
                }

                return $"https://{uri.Host}/{path}";
            }
            catch
            {
                return null;
            }
        }

        // https://github.com/org/repo.git or ADO
        if (remote.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || remote.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(remote);
                var path = uri.AbsolutePath.TrimEnd('/');
                if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                {
                    path = path[..^4];
                }

                var builder = new UriBuilder(uri.Scheme, uri.Host)
                {
                    Path = path,
                    Port = uri.IsDefaultPort ? -1 : uri.Port
                };
                return builder.Uri.ToString().TrimEnd('/');
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    public static string DetectHosting(string httpsRepoUrl)
    {
        if (httpsRepoUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase)
            || httpsRepoUrl.Contains("githubusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            return "github";
        }

        if (httpsRepoUrl.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase)
            || httpsRepoUrl.Contains("visualstudio.com", StringComparison.OrdinalIgnoreCase))
        {
            return "azure-devops";
        }

        // GitHub Enterprise / unknown — treat as github-like if path looks like org/repo
        return "unknown";
    }
}
