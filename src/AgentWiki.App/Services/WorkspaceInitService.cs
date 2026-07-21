using AgentWiki.Core;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.App.Services;

/// <summary>
/// Scaffolds <c>.agentwiki/workspace.json</c> and adds members.
/// </summary>
public sealed class WorkspaceInitService(
    IWorkspaceConfigLoader configLoader,
    ILogger<WorkspaceInitService> logger) : IWorkspaceInitService
{
    /// <inheritdoc />
    public async Task<WorkspaceInitResult> InitializeAsync(
        string workspaceRoot,
        string? name = null,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var root = PathUtility.ExpandAndResolve(workspaceRoot);
            if (!Directory.Exists(root))
            {
                return WorkspaceInitResult.Fail($"Workspace root does not exist: {root}");
            }

            var agentWikiDir = Path.Combine(root, Constants.Paths.ConfigDirectoryName);
            Directory.CreateDirectory(agentWikiDir);
            var configPath = Path.Combine(agentWikiDir, Constants.Paths.WorkspaceFileName);
            var created = new List<string>();

            if (File.Exists(configPath) && !force)
            {
                return WorkspaceInitResult.Ok(
                    $"Workspace config already exists at {configPath} (pass --force to overwrite).",
                    configPath,
                    []);
            }

            var displayName = string.IsNullOrWhiteSpace(name)
                ? Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : name.Trim();
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = "Workspace";
            }

            var config = CreateSampleConfig(displayName, root);
            await configLoader.SaveAsync(config, configPath, cancellationToken).ConfigureAwait(false);
            created.Add(Rel(root, configPath));

            // Ensure .agentwiki/.gitignore ignores last-run files (same as single-repo init).
            var gitignorePath = Path.Combine(agentWikiDir, ".gitignore");
            if (!File.Exists(gitignorePath) || force)
            {
                await File.WriteAllTextAsync(
                        gitignorePath,
                        """
                        # Local run state (commit config; keep secrets out of git)
                        last-run.json
                        workspace-last-run.json
                        *.local.json
                        """,
                        cancellationToken)
                    .ConfigureAwait(false);
                created.Add(Rel(root, gitignorePath));
            }

            logger.LogInformation("Initialized workspace '{Name}' at {Path}", displayName, configPath);
            return WorkspaceInitResult.Ok(
                $"Initialized workspace '{displayName}' at {configPath}. Edit members, then run `agent-wiki workspace generate`.",
                configPath,
                created);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Workspace init failed");
            return WorkspaceInitResult.Fail(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<WorkspaceInitResult> AddMemberAsync(
        string workspaceRoot,
        string pathOrRemote,
        string? memberId = null,
        string? label = null,
        string? branch = null,
        string? workspaceConfigPath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(pathOrRemote))
            {
                return WorkspaceInitResult.Fail("Path or remote URL is required.");
            }

            var source = pathOrRemote.Trim();
            var load = await configLoader
                .LoadAsync(workspaceRoot, workspaceConfigPath, cancellationToken)
                .ConfigureAwait(false);
            if (!load.Success || load.Config is null)
            {
                return WorkspaceInitResult.Fail(load.Error ?? "Failed to load workspace config.");
            }

            var config = load.Config;
            var explicitId = !string.IsNullOrWhiteSpace(memberId);
            var id = explicitId
                ? memberId!.Trim()
                : DeriveMemberId(source, config.Members.Select(m => m.Id));

            if (!WorkspaceConfigLoader.IsValidMemberId(id))
            {
                return WorkspaceInitResult.Fail(
                    $"Invalid member id '{id}'. Use letters, digits, '.', '-', or '_' only (exact repo name)"
                    + (explicitId ? "." : " (could not derive a valid id from the path/remote; pass --id)."));
            }

            if (config.Members.Any(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            {
                return WorkspaceInitResult.Fail(
                    $"Member id '{id}' already exists in the workspace."
                    + (explicitId ? "" : " Pass --id to choose a different id."));
            }

            var member = new WorkspaceMember
            {
                Id = id,
                Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
                Branch = string.IsNullOrWhiteSpace(branch) ? null : branch.Trim()
            };

            if (IsRemote(source))
            {
                member.Remote = source;
            }
            else
            {
                member.Path = source;
            }

            config.Members.Add(member);
            var validation = configLoader.Validate(config);
            if (!validation.Success)
            {
                return WorkspaceInitResult.Fail(validation.Error ?? "Validation failed after add.");
            }

            var path = config.ConfigFilePath
                       ?? Path.Combine(
                           PathUtility.ExpandAndResolve(workspaceRoot),
                           Constants.Paths.ConfigDirectoryName,
                           Constants.Paths.WorkspaceFileName);

            await configLoader.SaveAsync(config, path, cancellationToken).ConfigureAwait(false);
            var derivedNote = explicitId ? "" : " (id derived from path/remote)";
            logger.LogInformation("Added member {Id} to workspace at {Path}", member.Id, path);
            return WorkspaceInitResult.Ok(
                $"Added member '{member.Id}'{derivedNote} to {path}.",
                path,
                [path]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add workspace member");
            return WorkspaceInitResult.Fail(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<WorkspaceInitResult> RemoveMemberAsync(
        string workspaceRoot,
        string memberId,
        string? workspaceConfigPath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(memberId))
            {
                return WorkspaceInitResult.Fail("Member id is required. Example: agent-wiki workspace remove loan-service");
            }

            var load = await configLoader
                .LoadAsync(workspaceRoot, workspaceConfigPath, cancellationToken)
                .ConfigureAwait(false);
            if (!load.Success || load.Config is null)
            {
                return WorkspaceInitResult.Fail(load.Error ?? "Failed to load workspace config.");
            }

            var config = load.Config;
            var id = memberId.Trim();
            var index = config.Members.FindIndex(m =>
                m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                var known = config.Members.Count == 0
                    ? "(none)"
                    : string.Join(", ", config.Members.Select(m => m.Id));
                return WorkspaceInitResult.Fail(
                    $"Member id '{id}' not found in the workspace. Known members: {known}");
            }

            var removed = config.Members[index];
            config.Members.RemoveAt(index);

            var path = config.ConfigFilePath
                       ?? Path.Combine(
                           PathUtility.ExpandAndResolve(workspaceRoot),
                           Constants.Paths.ConfigDirectoryName,
                           Constants.Paths.WorkspaceFileName);

            await configLoader.SaveAsync(config, path, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Removed member {Id} from workspace at {Path}", removed.Id, path);

            var remaining = config.Members.Count;
            return WorkspaceInitResult.Ok(
                $"Removed member '{removed.Id}' from {path} ({remaining} member(s) remaining).",
                path,
                [path]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove workspace member");
            return WorkspaceInitResult.Fail(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<WorkspaceMemberListResult> ListMembersAsync(
        string workspaceRoot,
        string? workspaceConfigPath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var load = await configLoader
                .LoadAsync(workspaceRoot, workspaceConfigPath, cancellationToken)
                .ConfigureAwait(false);
            if (!load.Success || load.Config is null)
            {
                return WorkspaceMemberListResult.Fail(load.Error ?? "Failed to load workspace config.");
            }

            return WorkspaceMemberListResult.Ok(
                load.Config.Name,
                load.Config.ConfigFilePath,
                load.Config.Members,
                load.Warnings);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list workspace members");
            return WorkspaceMemberListResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Derives a stable member id from a local path or git remote URL:
    /// exact repository name (last path segment, strip <c>.git</c>), not kebab-cased.
    /// Collision suffix <c>-2</c>, <c>-3</c>, …
    /// </summary>
    public static string DeriveMemberId(string pathOrRemote, IEnumerable<string>? existingIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathOrRemote);
        var raw = pathOrRemote.Trim().TrimEnd('/', '\\');
        string candidate;

        if (IsRemote(raw))
        {
            candidate = ExtractRemoteName(raw);
        }
        else
        {
            var normalized = raw.Replace('\\', '/');
            var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            candidate = parts.Length > 0 ? parts[^1] : "member";
            if (candidate.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate[..^4];
            }
        }

        var baseId = SanitizeMemberId(candidate);
        return EnsureUniqueMemberId(baseId, existingIds);
    }

    /// <summary>
    /// Keeps exact repo name characters (letters, digits, <c>.</c>, <c>-</c>, <c>_</c>).
    /// Does not force lowercase or kebab-case.
    /// </summary>
    internal static string SanitizeMemberId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "member";
        }

        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var c in value.Trim())
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')
            {
                sb.Append(c);
            }
        }

        var s = sb.ToString().Trim('.', '-', '_');
        if (string.IsNullOrEmpty(s))
        {
            return "member";
        }

        if (s.Length > 128)
        {
            s = s[..128].TrimEnd('.', '-', '_');
        }

        return string.IsNullOrEmpty(s) ? "member" : s;
    }

    private static string EnsureUniqueMemberId(string baseId, IEnumerable<string>? existingIds)
    {
        var existing = new HashSet<string>(
            existingIds ?? [],
            StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(baseId))
        {
            return baseId;
        }

        for (var n = 2; n < 1000; n++)
        {
            var suffix = $"-{n}";
            var maxBase = Math.Max(1, 128 - suffix.Length);
            var truncated = baseId.Length <= maxBase ? baseId : baseId[..maxBase].TrimEnd('.', '-', '_');
            var candidate = truncated + suffix;
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }

        return baseId + "-" + Guid.NewGuid().ToString("N")[..8];
    }

    private static string ExtractRemoteName(string remote)
    {
        // git@github.com:org/Repo.git  or  https://github.com/org/Repo.git
        var s = remote.Trim();
        if (s.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            s = s[..^4];
        }

        s = s.TrimEnd('/', '\\');
        var slash = s.LastIndexOfAny(['/', ':']);
        if (slash >= 0 && slash < s.Length - 1)
        {
            return s[(slash + 1)..];
        }

        return s;
    }

    private static WorkspaceConfig CreateSampleConfig(string name, string root)
    {
        // Prefer sibling-style sample paths that are easy to edit.
        // Seed full memberDefaults (same surface as single-repo init).
        return new WorkspaceConfig
        {
            Name = name,
            Description = "Multi-repo system workspace (edit members, then run workspace generate).",
            OutputPath = Constants.Paths.DefaultWorkspaceOutputPath,
            AgentMdPath = Constants.Paths.DefaultAgentMdPath,
            GenerateAgentsMd = true,
            EnsureMemberWikis = true,
            MemberWikiPolicy = new MemberWikiPolicy
            {
                EnsureMissing = Constants.Workspace.EnsureMissingDefault,
                UpdateMembers = Constants.Workspace.DefaultUpdateMembers
            },
            MemberDefaults = AgentWikiConfigDefaults.CreateFullTemplate(),
            WorkspaceRoot = root,
            Members =
            [
                new WorkspaceMember
                {
                    Id = "ServiceA",
                    Path = "../ServiceA",
                    Label = "Service A",
                    Role = "service",
                    Notes = "Replace with a real local path or remote."
                },
                new WorkspaceMember
                {
                    Id = "SharedLib",
                    Path = "../SharedLib",
                    Label = "Shared Library",
                    Role = "library"
                }
            ]
        };
    }

    private static bool IsRemote(string value) =>
        value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("git@", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)
        || value.EndsWith(".git", StringComparison.OrdinalIgnoreCase);

    private static string Rel(string root, string absolute) =>
        Path.GetRelativePath(root, absolute).Replace('\\', '/');
}
