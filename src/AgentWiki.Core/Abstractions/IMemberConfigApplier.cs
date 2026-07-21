using AgentWiki.Core.Models;

namespace AgentWiki.Core.Abstractions;

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
