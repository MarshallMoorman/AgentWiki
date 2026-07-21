using System.Text.Json;
using System.Text.Json.Serialization;
using AgentWiki.Core;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.App.Services;

/// <summary>
/// Loads and validates <c>.agentwiki/workspace.json</c> (or an explicit path).
/// </summary>
public sealed class WorkspaceConfigLoader(ILogger<WorkspaceConfigLoader> logger) : IWorkspaceConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <inheritdoc />
    public async Task<WorkspaceLoadResult> LoadAsync(
        string workspaceRoot,
        string? workspaceConfigPath = null,
        CancellationToken cancellationToken = default)
    {
        var root = PathUtility.ExpandAndResolve(workspaceRoot);
        string configPath;
        if (!string.IsNullOrWhiteSpace(workspaceConfigPath))
        {
            configPath = PathUtility.ExpandAndResolve(workspaceConfigPath);
        }
        else
        {
            configPath = Path.Combine(root, Constants.Paths.ConfigDirectoryName, Constants.Paths.WorkspaceFileName);
        }

        if (!File.Exists(configPath))
        {
            return WorkspaceLoadResult.Fail(
                $"Workspace config not found at '{configPath}'. Run `agent-wiki workspace init` first.");
        }

        try
        {
            await using var stream = File.OpenRead(configPath);
            var config = await JsonSerializer
                .DeserializeAsync<WorkspaceConfig>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (config is null)
            {
                return WorkspaceLoadResult.Fail($"Failed to parse workspace config at '{configPath}'.");
            }

            config.WorkspaceRoot = root;
            config.ConfigFilePath = configPath;
            if (string.IsNullOrWhiteSpace(config.OutputPath))
            {
                config.OutputPath = Constants.Paths.DefaultWorkspaceOutputPath;
            }

            if (string.IsNullOrWhiteSpace(config.AgentMdPath))
            {
                config.AgentMdPath = Constants.Paths.DefaultAgentMdPath;
            }

            // Normalize member wiki policy + legacy ensureMemberWikis.
            config.MemberWikiPolicy ??= new MemberWikiPolicy();
            if (string.IsNullOrWhiteSpace(config.MemberWikiPolicy.UpdateMembers))
            {
                config.MemberWikiPolicy.UpdateMembers = Constants.Workspace.DefaultUpdateMembers;
            }

            logger.LogInformation(
                "Loaded workspace '{Name}' with {Count} member(s) from {Path}",
                config.Name,
                config.Members.Count,
                configPath);

            return Validate(config);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Invalid workspace JSON at {Path}", configPath);
            return WorkspaceLoadResult.Fail($"Invalid workspace JSON at '{configPath}': {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load workspace config from {Path}", configPath);
            return WorkspaceLoadResult.Fail($"Failed to load workspace config: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public WorkspaceLoadResult Validate(WorkspaceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var warnings = new List<string>();
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.Name))
        {
            config.Name = "Workspace";
            warnings.Add("Workspace name was empty; defaulted to 'Workspace'.");
        }

        if (config.Members.Count == 0)
        {
            // Allow empty config while editing (add/remove); generate fails when nothing resolves.
            warnings.Add("Workspace has no members. Add at least one member (path or remote).");
        }

        if (config.Members.Count > Constants.Config.MaxWorkspaceMembers)
        {
            errors.Add(
                $"Workspace has {config.Members.Count} members; max is {Constants.Config.MaxWorkspaceMembers}.");
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < config.Members.Count; i++)
        {
            var m = config.Members[i];
            if (string.IsNullOrWhiteSpace(m.Id))
            {
                errors.Add($"Member at index {i} is missing a required 'id'.");
                continue;
            }

            m.Id = m.Id.Trim();
            if (!IsValidMemberId(m.Id))
            {
                errors.Add(
                    $"Member id '{m.Id}' is invalid. Use letters, digits, '.', '-', or '_' only (exact repo name).");
            }

            if (!seenIds.Add(m.Id))
            {
                errors.Add($"Duplicate member id '{m.Id}'.");
            }

            var hasPath = !string.IsNullOrWhiteSpace(m.Path);
            var hasRemote = !string.IsNullOrWhiteSpace(m.Remote);
            if (!hasPath && !hasRemote)
            {
                errors.Add($"Member '{m.Id}' must specify either 'path' or 'remote'.");
            }

            if (hasPath && hasRemote)
            {
                warnings.Add(
                    $"Member '{m.Id}' has both path and remote; local path will be preferred.");
            }

            if (hasRemote && !LooksLikeGitRemote(m.Remote!))
            {
                warnings.Add(
                    $"Member '{m.Id}' remote '{m.Remote}' does not look like a git URL (https://… or git@…).");
            }

            if (string.IsNullOrWhiteSpace(m.WikiPath))
            {
                m.WikiPath = Constants.Paths.DefaultOutputPath;
            }
        }

        config.MemberWikiPolicy ??= new MemberWikiPolicy();
        var updatePolicy = (config.MemberWikiPolicy.UpdateMembers ?? "").Trim().ToLowerInvariant();
        if (updatePolicy is not (
            Constants.Workspace.UpdateMembersNever
            or Constants.Workspace.UpdateMembersStale
            or Constants.Workspace.UpdateMembersAll))
        {
            warnings.Add(
                $"memberWikiPolicy.updateMembers '{config.MemberWikiPolicy.UpdateMembers}' is invalid; "
                + $"using '{Constants.Workspace.DefaultUpdateMembers}'.");
            config.MemberWikiPolicy.UpdateMembers = Constants.Workspace.DefaultUpdateMembers;
        }
        else
        {
            config.MemberWikiPolicy.UpdateMembers = updatePolicy;
        }

        // Secret warnings for memberDefaults (never log values).
        if (config.MemberDefaults is not null)
        {
            foreach (var secretNote in AgentWiki.Core.Generation.AgentWikiConfigDefaults.DescribeSecretsPresent(config.MemberDefaults))
            {
                warnings.Add(secretNote + " Prefer env vars; do not commit secrets.");
            }
        }
        else
        {
            warnings.Add(
                "workspace.json has no memberDefaults; member init/replace-configs will need a defaults section.");
        }

        if (errors.Count > 0)
        {
            return WorkspaceLoadResult.Fail(string.Join(" ", errors));
        }

        return WorkspaceLoadResult.Ok(config, warnings);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        WorkspaceConfig config,
        string configFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(configFilePath);

        var path = PathUtility.ExpandAndResolve(configFilePath);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Persist only serializable fields (ignore runtime WorkspaceRoot / ConfigFilePath).
        var dto = new WorkspaceConfig
        {
            Name = config.Name,
            Description = config.Description,
            OutputPath = config.OutputPath,
            AgentMdPath = config.AgentMdPath,
            GenerateAgentsMd = config.GenerateAgentsMd,
            EnsureMemberWikis = config.EnsureMemberWikis,
            MemberWikiPolicy = config.MemberWikiPolicy ?? new MemberWikiPolicy(),
            MemberDefaults = config.MemberDefaults,
            Members = config.Members.Select(m => new WorkspaceMember
            {
                Id = m.Id,
                Label = m.Label,
                Role = m.Role,
                Path = m.Path,
                Remote = m.Remote,
                Branch = m.Branch,
                Commit = m.Commit,
                WikiPath = m.WikiPath,
                Notes = m.Notes
            }).ToList(),
            IgnorePatterns = [..config.IgnorePatterns],
            SystemPromptOverrides = new Dictionary<string, string>(
                config.SystemPromptOverrides,
                StringComparer.OrdinalIgnoreCase)
        };

        var json = JsonSerializer.Serialize(dto, JsonOptions) + Environment.NewLine;
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Wrote workspace config to {Path}", path);
    }

    /// <summary>
    /// Member ids are exact repo names: letters, digits, <c>.</c>, <c>-</c>, <c>_</c> (max 128).
    /// Forbidden: path separators, spaces, and other punctuation.
    /// </summary>
    internal static bool IsValidMemberId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128)
        {
            return false;
        }

        foreach (var c in id)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool LooksLikeGitRemote(string remote)
    {
        remote = remote.Trim();
        return remote.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
               || remote.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || remote.StartsWith("git@", StringComparison.OrdinalIgnoreCase)
               || remote.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)
               || remote.EndsWith(".git", StringComparison.OrdinalIgnoreCase);
    }
}
