using System.Text.Json;
using System.Text.Json.Serialization;
using AgentWiki.Core;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.App.Services;

/// <summary>
/// Writes <c>memberDefaults</c> from workspace.json into member <c>.agentwiki/config.json</c>
/// (init copy and force replace-configs).
/// </summary>
public interface IMemberConfigApplier
{
    /// <summary>
    /// Writes memberDefaults into a single local member when config is missing (init path).
    /// Does nothing if config already exists unless <paramref name="forceReplace"/> is true.
    /// </summary>
    Task<MemberConfigApplyResult> ApplyToMemberAsync(
        string memberAbsolutePath,
        AgentWikiConfig memberDefaults,
        bool forceReplace = false,
        bool dryRun = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Force-replaces config.json for selected local members from memberDefaults.
    /// </summary>
    Task<MemberConfigReplaceBatchResult> ReplaceConfigsAsync(
        WorkspaceConfig workspace,
        IReadOnlyList<ResolvedWorkspaceMember> members,
        string? onlyMemberId = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default);
}

/// <summary>Outcome of applying defaults to one member.</summary>
public sealed class MemberConfigApplyResult
{
    public required bool Success { get; init; }
    public string Message { get; init; } = "";
    public string? ConfigPath { get; init; }
    public bool Wrote { get; init; }
    public bool Skipped { get; init; }
    public bool WouldWrite { get; init; }
    public string? Error { get; init; }

    public static MemberConfigApplyResult OkWrote(string path, bool dryRun) =>
        new()
        {
            Success = true,
            Wrote = !dryRun,
            WouldWrite = dryRun,
            ConfigPath = path,
            Message = dryRun
                ? $"[dry-run] Would write member config at {path}"
                : $"Wrote member config at {path}"
        };

    public static MemberConfigApplyResult OkSkipped(string path, string reason) =>
        new()
        {
            Success = true,
            Skipped = true,
            ConfigPath = path,
            Message = reason
        };

    public static MemberConfigApplyResult Fail(string error) =>
        new() { Success = false, Error = error, Message = error };
}

/// <summary>Batch outcome for replace-configs.</summary>
public sealed class MemberConfigReplaceBatchResult
{
    public required bool Success { get; init; }
    public string Message { get; init; } = "";
    public string? Error { get; init; }
    public bool DryRun { get; init; }
    public IReadOnlyList<MemberConfigApplyResult> Results { get; init; } = [];
    public int WroteCount { get; init; }
    public int WouldWriteCount { get; init; }
    public int SkippedCount { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <inheritdoc />
public sealed class MemberConfigApplier(ILogger<MemberConfigApplier> logger) : IMemberConfigApplier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <inheritdoc />
    public async Task<MemberConfigApplyResult> ApplyToMemberAsync(
        string memberAbsolutePath,
        AgentWikiConfig memberDefaults,
        bool forceReplace = false,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberAbsolutePath);
        ArgumentNullException.ThrowIfNull(memberDefaults);

        try
        {
            var root = PathUtility.ExpandAndResolve(memberAbsolutePath);
            if (!Directory.Exists(root))
            {
                return MemberConfigApplyResult.Fail($"Member path does not exist: {root}");
            }

            var agentWikiDir = Path.Combine(root, Constants.Paths.ConfigDirectoryName);
            var configPath = Path.Combine(agentWikiDir, Constants.Paths.ConfigFileName);

            if (File.Exists(configPath) && !forceReplace)
            {
                return MemberConfigApplyResult.OkSkipped(
                    configPath,
                    $"Member config already exists at {configPath} (not overwritten; use replace-configs to force).");
            }

            if (dryRun)
            {
                var action = File.Exists(configPath) ? "overwrite" : "create";
                logger.LogInformation("[dry-run] Would {Action} {Path}", action, configPath);
                return MemberConfigApplyResult.OkWrote(configPath, dryRun: true);
            }

            Directory.CreateDirectory(agentWikiDir);
            var toWrite = AgentWikiConfigDefaults.CloneForMember(memberDefaults);
            var json = JsonSerializer.Serialize(toWrite, JsonOptions) + Environment.NewLine;
            await File.WriteAllTextAsync(configPath, json, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                forceReplace
                    ? "Replaced member config at {Path} from memberDefaults"
                    : "Applied memberDefaults (init) at {Path}",
                configPath);

            return MemberConfigApplyResult.OkWrote(configPath, dryRun: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply memberDefaults to {Path}", memberAbsolutePath);
            return MemberConfigApplyResult.Fail(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<MemberConfigReplaceBatchResult> ReplaceConfigsAsync(
        WorkspaceConfig workspace,
        IReadOnlyList<ResolvedWorkspaceMember> members,
        string? onlyMemberId = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(members);

        var warnings = new List<string>();
        if (workspace.MemberDefaults is null)
        {
            return new MemberConfigReplaceBatchResult
            {
                Success = false,
                Error = "workspace.json has no memberDefaults section. Run workspace init or add memberDefaults first.",
                Message = "replace-configs failed",
                DryRun = dryRun
            };
        }

        warnings.AddRange(AgentWikiConfigDefaults.DescribeSecretsPresent(workspace.MemberDefaults)
            .Select(s => $"Warning: {s} (prefer env vars; never commit secrets)."));

        var results = new List<MemberConfigApplyResult>();
        var wrote = 0;
        var would = 0;
        var skipped = 0;

        foreach (var member in members)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(onlyMemberId)
                && !member.Definition.Id.Equals(onlyMemberId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Only local full clones with a configured path (not remote-only cache).
            if (member.IsRemote || string.IsNullOrWhiteSpace(member.Definition.Path))
            {
                var skip = MemberConfigApplyResult.OkSkipped(
                    member.AbsolutePath,
                    $"Skipped remote-only member '{member.Definition.Id}' (replace-configs applies to local full clones only).");
                results.Add(skip);
                skipped++;
                warnings.Add(skip.Message);
                continue;
            }

            if (!member.Success)
            {
                var fail = MemberConfigApplyResult.Fail(
                    $"Member '{member.Definition.Id}' did not resolve: {member.Error}");
                results.Add(fail);
                skipped++;
                warnings.Add(fail.Message);
                continue;
            }

            var apply = await ApplyToMemberAsync(
                    member.AbsolutePath,
                    workspace.MemberDefaults,
                    forceReplace: true,
                    dryRun,
                    cancellationToken)
                .ConfigureAwait(false);
            results.Add(apply);
            if (apply.Wrote)
            {
                wrote++;
            }
            else if (apply.WouldWrite)
            {
                would++;
            }
            else if (apply.Skipped || !apply.Success)
            {
                skipped++;
            }
        }

        if (!string.IsNullOrWhiteSpace(onlyMemberId)
            && results.Count == 0)
        {
            return new MemberConfigReplaceBatchResult
            {
                Success = false,
                Error = $"No member matched id '{onlyMemberId}'.",
                Message = "replace-configs failed",
                DryRun = dryRun,
                Warnings = warnings
            };
        }

        var msg = dryRun
            ? $"[dry-run] Would replace {would} member config(s); skipped {skipped}."
            : $"Replaced {wrote} member config(s) from memberDefaults; skipped {skipped}.";

        return new MemberConfigReplaceBatchResult
        {
            Success = true,
            Message = msg,
            DryRun = dryRun,
            Results = results,
            WroteCount = wrote,
            WouldWriteCount = would,
            SkippedCount = skipped,
            Warnings = warnings
        };
    }
}
