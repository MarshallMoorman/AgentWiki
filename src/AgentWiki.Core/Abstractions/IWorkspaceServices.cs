using AgentWiki.Core.Models;

namespace AgentWiki.Core.Abstractions;

/// <summary>
/// Loads and validates multi-repo workspace definitions (<c>.agentwiki/workspace.json</c>).
/// </summary>
public interface IWorkspaceConfigLoader
{
    /// <summary>
    /// Loads a workspace config from an explicit file path or discovers
    /// <c>.agentwiki/workspace.json</c> under <paramref name="workspaceRoot"/>.
    /// </summary>
    Task<WorkspaceLoadResult> LoadAsync(
        string workspaceRoot,
        string? workspaceConfigPath = null,
        CancellationToken cancellationToken = default);

    /// <summary>Validates a workspace config (duplicate ids, missing sources, etc.).</summary>
    WorkspaceLoadResult Validate(WorkspaceConfig config);

    /// <summary>
    /// Saves <paramref name="config"/> to disk (used by init/add).
    /// Secrets must never be written.
    /// </summary>
    Task SaveAsync(WorkspaceConfig config, string configFilePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves workspace members to absolute local paths (local path or remote shallow clone/cache).
/// </summary>
public interface IWorkspaceMemberResolver
{
    /// <summary>Resolves all members; continues on individual failures and records errors.</summary>
    Task<IReadOnlyList<ResolvedWorkspaceMember>> ResolveAllAsync(
        WorkspaceConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves a single member.</summary>
    Task<ResolvedWorkspaceMember> ResolveAsync(
        WorkspaceConfig config,
        WorkspaceMember member,
        CancellationToken cancellationToken = default);
}

/// <summary>Scaffolds a workspace definition file.</summary>
public interface IWorkspaceInitService
{
    Task<WorkspaceInitResult> InitializeAsync(
        string workspaceRoot,
        string? name = null,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a member to an existing workspace definition and saves.
    /// When <paramref name="memberId"/> is null/empty, derives a stable id from the path or remote URL.
    /// </summary>
    Task<WorkspaceInitResult> AddMemberAsync(
        string workspaceRoot,
        string pathOrRemote,
        string? memberId = null,
        string? label = null,
        string? branch = null,
        string? workspaceConfigPath = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Collects cross-repo signals from resolved members (file heuristics only).</summary>
public interface ICrossRepoSignalCollector
{
    Task<CrossRepoSignals> CollectAsync(
        IReadOnlyList<WorkspaceMemberAnalysis> members,
        CancellationToken cancellationToken = default);
}

/// <summary>Inspects per-member wiki freshness.</summary>
public interface IMemberWikiInspector
{
    MemberWikiStatus Inspect(ResolvedWorkspaceMember member, int staleDays = 0);
}

/// <summary>
/// Orchestrates multi-repo system wiki generation (file-based Phase 1).
/// Reuses single-repo <see cref="IWikiGenerator"/> for member wikis.
/// </summary>
public interface IWorkspaceOrchestrator
{
    Task<WorkspaceGenerationResult> GenerateAsync(
        WorkspaceGenerationRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceStatusResult> GetStatusAsync(
        string workspaceRoot,
        string? workspaceConfigPath = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Persists workspace-level last-run state for incremental updates.</summary>
public interface IWorkspaceLastRunStore
{
    Task<WorkspaceLastRunState?> LoadAsync(string workspaceRoot, CancellationToken cancellationToken = default);

    Task SaveAsync(
        string workspaceRoot,
        WorkspaceLastRunState state,
        CancellationToken cancellationToken = default);
}
