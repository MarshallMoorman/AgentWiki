using AgentWiki.Core;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
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
        string memberId,
        string pathOrRemote,
        string? label = null,
        string? branch = null,
        string? workspaceConfigPath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(memberId))
            {
                return WorkspaceInitResult.Fail("Member id is required.");
            }

            if (!WorkspaceConfigLoader.IsValidMemberId(memberId.Trim()))
            {
                return WorkspaceInitResult.Fail(
                    $"Invalid member id '{memberId}'. Use letters, digits, hyphens, or underscores only.");
            }

            if (string.IsNullOrWhiteSpace(pathOrRemote))
            {
                return WorkspaceInitResult.Fail("Path or remote URL is required.");
            }

            var load = await configLoader
                .LoadAsync(workspaceRoot, workspaceConfigPath, cancellationToken)
                .ConfigureAwait(false);
            if (!load.Success || load.Config is null)
            {
                return WorkspaceInitResult.Fail(load.Error ?? "Failed to load workspace config.");
            }

            var config = load.Config;
            if (config.Members.Any(m => m.Id.Equals(memberId.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return WorkspaceInitResult.Fail($"Member id '{memberId}' already exists in the workspace.");
            }

            var member = new WorkspaceMember
            {
                Id = memberId.Trim(),
                Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
                Branch = string.IsNullOrWhiteSpace(branch) ? null : branch.Trim()
            };

            var source = pathOrRemote.Trim();
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
            logger.LogInformation("Added member {Id} to workspace at {Path}", member.Id, path);
            return WorkspaceInitResult.Ok(
                $"Added member '{member.Id}' to {path}.",
                path,
                [path]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add workspace member");
            return WorkspaceInitResult.Fail(ex.Message);
        }
    }

    private static WorkspaceConfig CreateSampleConfig(string name, string root)
    {
        // Prefer sibling-style sample paths that are easy to edit.
        return new WorkspaceConfig
        {
            Name = name,
            Description = "Multi-repo system workspace (edit members, then run workspace generate).",
            OutputPath = Constants.Paths.DefaultWorkspaceOutputPath,
            AgentMdPath = Constants.Paths.DefaultAgentMdPath,
            GenerateAgentsMd = true,
            EnsureMemberWikis = true,
            WorkspaceRoot = root,
            Members =
            [
                new WorkspaceMember
                {
                    Id = "service-a",
                    Path = "../ServiceA",
                    Label = "Service A",
                    Role = "service",
                    Notes = "Replace with a real local path or remote."
                },
                new WorkspaceMember
                {
                    Id = "shared-lib",
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
