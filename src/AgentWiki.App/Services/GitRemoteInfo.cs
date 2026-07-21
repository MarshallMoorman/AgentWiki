namespace AgentWiki.App.Services;

/// <summary>
/// Resolves preferred git remote URL and current branch for web link building.
/// Prefer tracked upstream for current branch → origin → first remote.
/// </summary>
public static class GitRemoteInfo
{
    public sealed record Result(
        string? RemoteUrl,
        string? RemoteName,
        string? Branch,
        IReadOnlyList<string> Warnings);

    public static async Task<Result> ResolveAsync(
        string repoPath,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        if (!GitProcess.IsGitRepository(repoPath))
        {
            warnings.Add("Not a git repository.");
            return new Result(null, null, null, warnings);
        }

        string? branch = null;
        try
        {
            var b = await GitProcess
                .RunAsync(repoPath, ["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken)
                .ConfigureAwait(false);
            branch = string.IsNullOrWhiteSpace(b) ? null : b.Trim();
            if (branch is "HEAD")
            {
                warnings.Add("Detached HEAD; web links may use default branch name.");
                branch = null;
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not resolve branch: {ex.Message}");
        }

        string? remoteName = null;
        string? remoteUrl = null;

        // 1) Upstream of current branch
        if (!string.IsNullOrWhiteSpace(branch))
        {
            try
            {
                var upstream = await GitProcess
                    .RunAsync(
                        repoPath,
                        ["rev-parse", "--abbrev-ref", $"{branch}@{{upstream}}"],
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(upstream))
                {
                    var u = upstream.Trim(); // e.g. origin/main
                    var slash = u.IndexOf('/');
                    if (slash > 0)
                    {
                        remoteName = u[..slash];
                    }
                }
            }
            catch
            {
                // no upstream
            }
        }

        // 2) origin
        remoteName ??= "origin";

        try
        {
            remoteUrl = await TryGetRemoteUrlAsync(repoPath, remoteName, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            remoteUrl = null;
        }

        // 3) first remote
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            try
            {
                var remotes = await GitProcess
                    .RunAsync(repoPath, ["remote"], cancellationToken)
                    .ConfigureAwait(false);
                var first = remotes?
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(first))
                {
                    remoteName = first;
                    remoteUrl = await TryGetRemoteUrlAsync(repoPath, first, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not list remotes: {ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            warnings.Add("No git remote URL found.");
        }

        return new Result(remoteUrl, remoteName, branch, warnings);
    }

    private static async Task<string?> TryGetRemoteUrlAsync(
        string repoPath,
        string remoteName,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = await GitProcess
                .RunAsync(repoPath, ["remote", "get-url", remoteName], cancellationToken)
                .ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(url) ? null : url.Trim();
        }
        catch
        {
            return null;
        }
    }
}
