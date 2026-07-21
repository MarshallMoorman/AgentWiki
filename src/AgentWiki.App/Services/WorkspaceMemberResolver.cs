using System.Security.Cryptography;
using System.Text;
using AgentWiki.Core;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.App.Services;

/// <summary>
/// Resolves local workspace members and shallow-clones remote members into
/// <c>~/.agentwiki/cache/workspaces/&lt;workspace&gt;/&lt;member&gt;/</c>.
/// </summary>
public sealed class WorkspaceMemberResolver(ILogger<WorkspaceMemberResolver> logger) : IWorkspaceMemberResolver
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ResolvedWorkspaceMember>> ResolveAllAsync(
        WorkspaceConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        var list = new List<ResolvedWorkspaceMember>(config.Members.Count);
        foreach (var member in config.Members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            list.Add(await ResolveAsync(config, member, cancellationToken).ConfigureAwait(false));
        }

        return list;
    }

    /// <inheritdoc />
    public async Task<ResolvedWorkspaceMember> ResolveAsync(
        WorkspaceConfig config,
        WorkspaceMember member,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(member);

        var warnings = new List<string>();
        var root = string.IsNullOrWhiteSpace(config.WorkspaceRoot)
            ? Directory.GetCurrentDirectory()
            : PathUtility.ExpandAndResolve(config.WorkspaceRoot);

        // Prefer local path when both are set.
        if (!string.IsNullOrWhiteSpace(member.Path))
        {
            var expanded = PathUtility.ExpandHome(member.Path.Trim());
            var absolute = Path.IsPathRooted(expanded)
                ? Path.GetFullPath(expanded)
                : Path.GetFullPath(Path.Combine(root, expanded));

            if (!Directory.Exists(absolute))
            {
                return new ResolvedWorkspaceMember
                {
                    Definition = member,
                    AbsolutePath = absolute,
                    IsRemote = false,
                    Error =
                        $"Member '{member.Id}' path does not exist: {absolute}. "
                        + "Check the workspace member path (relative paths are resolved from the workspace root)."
                };
            }

            var sha = await GitProcess.TryGetHeadShaAsync(absolute, cancellationToken).ConfigureAwait(false);
            logger.LogDebug("Resolved local member {Id} → {Path}", member.Id, absolute);
            return new ResolvedWorkspaceMember
            {
                Definition = member,
                AbsolutePath = absolute,
                IsRemote = false,
                HeadSha = sha,
                Warnings = warnings
            };
        }

        if (string.IsNullOrWhiteSpace(member.Remote))
        {
            return new ResolvedWorkspaceMember
            {
                Definition = member,
                AbsolutePath = "",
                IsRemote = true,
                Error = $"Member '{member.Id}' has neither path nor remote."
            };
        }

        try
        {
            var cacheRoot = GetMemberCachePath(config, member);
            Directory.CreateDirectory(Path.GetDirectoryName(cacheRoot)!);

            if (!Directory.Exists(Path.Combine(cacheRoot, ".git")))
            {
                logger.LogInformation(
                    "Shallow-cloning remote member {Id} from {Remote} into {Cache}",
                    member.Id,
                    RedactRemote(member.Remote),
                    cacheRoot);

                if (Directory.Exists(cacheRoot))
                {
                    // Incomplete prior attempt
                    try
                    {
                        Directory.Delete(cacheRoot, recursive: true);
                    }
                    catch
                    {
                        // best effort
                    }
                }

                var cloneArgs = new List<string> { "clone", "--depth", "1" };
                if (!string.IsNullOrWhiteSpace(member.Branch))
                {
                    cloneArgs.Add("--branch");
                    cloneArgs.Add(member.Branch.Trim());
                }

                cloneArgs.Add(member.Remote.Trim());
                cloneArgs.Add(cacheRoot);

                await GitProcess
                    .RunAsync(Directory.GetCurrentDirectory(), cloneArgs, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                logger.LogDebug("Updating remote member cache for {Id} at {Cache}", member.Id, cacheRoot);
                try
                {
                    // Fetch latest for the pinned branch (or default).
                    await GitProcess
                        .RunAsync(cacheRoot, ["fetch", "--depth", "1", "origin"], cancellationToken)
                        .ConfigureAwait(false);

                    var branch = string.IsNullOrWhiteSpace(member.Branch) ? null : member.Branch.Trim();
                    if (branch is not null)
                    {
                        await GitProcess
                            .RunAsync(cacheRoot, ["checkout", branch], cancellationToken)
                            .ConfigureAwait(false);
                        try
                        {
                            await GitProcess
                                .RunAsync(cacheRoot, ["reset", "--hard", $"origin/{branch}"], cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            warnings.Add(
                                $"Member '{member.Id}': could not hard-reset to origin/{branch}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add(
                        $"Member '{member.Id}': cache exists but fetch/update failed ({ex.Message}). Using existing clone.");
                    logger.LogWarning(ex, "Failed to update remote cache for {Id}", member.Id);
                }
            }

            if (!string.IsNullOrWhiteSpace(member.Commit))
            {
                try
                {
                    await GitProcess
                        .RunAsync(cacheRoot, ["fetch", "--depth", "1", "origin", member.Commit.Trim()], cancellationToken)
                        .ConfigureAwait(false);
                    await GitProcess
                        .RunAsync(cacheRoot, ["checkout", member.Commit.Trim()], cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return new ResolvedWorkspaceMember
                    {
                        Definition = member,
                        AbsolutePath = cacheRoot,
                        IsRemote = true,
                        CachePath = cacheRoot,
                        Error =
                            $"Member '{member.Id}': failed to checkout commit '{member.Commit}': {ex.Message}"
                    };
                }
            }

            var head = await GitProcess.TryGetHeadShaAsync(cacheRoot, cancellationToken).ConfigureAwait(false);
            return new ResolvedWorkspaceMember
            {
                Definition = member,
                AbsolutePath = cacheRoot,
                IsRemote = true,
                CachePath = cacheRoot,
                ResolvedBranch = member.Branch,
                HeadSha = head,
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve remote member {Id}", member.Id);
            return new ResolvedWorkspaceMember
            {
                Definition = member,
                AbsolutePath = "",
                IsRemote = true,
                Error =
                    $"Member '{member.Id}': failed to clone/update remote '{RedactRemote(member.Remote)}': {ex.Message}. "
                    + "Ensure git is installed and credentials (if private) are available."
            };
        }
    }

    /// <summary>Cache path: ~/.agentwiki/cache/workspaces/&lt;workspace-key&gt;/&lt;member-id&gt;/</summary>
    public static string GetMemberCachePath(WorkspaceConfig config, WorkspaceMember member)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Path.GetTempPath();
        }

        var workspaceKey = BuildWorkspaceCacheKey(config);
        return Path.Combine(
            home,
            Constants.Paths.ConfigDirectoryName,
            "cache",
            Constants.Paths.WorkspaceCacheDirectoryName,
            workspaceKey,
            SanitizePathSegment(member.Id));
    }

    private static string BuildWorkspaceCacheKey(WorkspaceConfig config)
    {
        var name = string.IsNullOrWhiteSpace(config.Name) ? "workspace" : config.Name.Trim();
        var root = config.WorkspaceRoot ?? "";
        var material = $"{name}|{root}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..12].ToLowerInvariant();
        return $"{SanitizePathSegment(name)}-{hash}";
    }

    private static string SanitizePathSegment(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value.Trim())
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            {
                sb.Append(c);
            }
            else if (char.IsWhiteSpace(c) || c is '.' or '/')
            {
                sb.Append('-');
            }
        }

        var s = sb.ToString().Trim('-');
        return string.IsNullOrEmpty(s) ? "member" : s;
    }

    /// <summary>Avoid logging credentials embedded in git URLs.</summary>
    public static string RedactRemote(string remote)
    {
        try
        {
            if (Uri.TryCreate(remote, UriKind.Absolute, out var uri)
                && (!string.IsNullOrEmpty(uri.UserInfo)))
            {
                var builder = new UriBuilder(uri) { UserName = "****", Password = "****" };
                return builder.Uri.ToString();
            }
        }
        catch
        {
            // fall through
        }

        return remote;
    }
}
