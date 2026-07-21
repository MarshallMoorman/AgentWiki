// Constants lives in AgentWiki.Core (not Models).
using Constants = AgentWiki.Core.Constants;

namespace AgentWiki.Core.Models;

/// <summary>
/// Multi-repo workspace definition (file-based Phase 1).
/// Stored as <c>.agentwiki/workspace.json</c> by default.
/// </summary>
public sealed class WorkspaceConfig
{
    /// <summary>Human-readable workspace name.</summary>
    public string Name { get; set; } = "Workspace";

    /// <summary>Optional description of the system / platform.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// System wiki output directory relative to the workspace root
    /// (default: <c>docs/knowledge-base</c>).
    /// </summary>
    public string OutputPath { get; set; } = Constants.Paths.DefaultWorkspaceOutputPath;

    /// <summary>Path to workspace-level AGENTS.md relative to the workspace root.</summary>
    public string AgentMdPath { get; set; } = Constants.Paths.DefaultAgentMdPath;

    /// <summary>
    /// When true (default), generate/refresh workspace AGENTS.md during generate/update.
    /// </summary>
    public bool GenerateAgentsMd { get; set; } = true;

    /// <summary>
    /// When true (default), run per-member wiki generate/update when a member wiki is missing or stale.
    /// When false, only warn and continue with system pages from available signals.
    /// </summary>
    public bool EnsureMemberWikis { get; set; } = true;

    /// <summary>Member repositories that form this workspace.</summary>
    public List<WorkspaceMember> Members { get; set; } = [];

    /// <summary>Additional ignore patterns applied when scanning members (optional).</summary>
    public List<string> IgnorePatterns { get; set; } = [];

    /// <summary>Optional free-form prompt overrides for future LLM system generation (Phase 1 offline-first).</summary>
    public Dictionary<string, string> SystemPromptOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Absolute path of the workspace root (directory containing or owning the definition).
    /// Filled by the loader; not persisted in JSON.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string WorkspaceRoot { get; set; } = ".";

    /// <summary>
    /// Absolute path of the loaded definition file.
    /// Filled by the loader; not persisted in JSON.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ConfigFilePath { get; set; }
}

/// <summary>A single repository participating in a workspace.</summary>
public sealed class WorkspaceMember
{
    /// <summary>Stable unique id used in output paths and last-run state (e.g. <c>loan-service</c>).</summary>
    public string Id { get; set; } = "";

    /// <summary>Display label (defaults to <see cref="Id"/> when empty).</summary>
    public string? Label { get; set; }

    /// <summary>Optional role hint: service, library, frontend, shared, infrastructure, etc.</summary>
    public string? Role { get; set; }

    /// <summary>
    /// Local path to the member repository (relative to workspace root or absolute).
    /// Mutually exclusive with <see cref="Remote"/> (prefer path when both set).
    /// </summary>
    public string? Path { get; set; }

    /// <summary>Git remote URL for clone/cache when the member is not local.</summary>
    public string? Remote { get; set; }

    /// <summary>Optional branch for remote members (default: remote HEAD / main).</summary>
    public string? Branch { get; set; }

    /// <summary>Optional commit pin for remote members.</summary>
    public string? Commit { get; set; }

    /// <summary>
    /// Per-member wiki path relative to the member repo root (default: <c>docs/wiki</c>).
    /// </summary>
    public string WikiPath { get; set; } = Constants.Paths.DefaultOutputPath;

    /// <summary>Optional notes shown on the member summary page.</summary>
    public string? Notes { get; set; }

    /// <summary>Resolved display label.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Label) ? (string.IsNullOrWhiteSpace(Id) ? "member" : Id) : Label;
}

/// <summary>Result of loading and validating a workspace config.</summary>
public sealed class WorkspaceLoadResult
{
    public required bool Success { get; init; }
    public WorkspaceConfig? Config { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public static WorkspaceLoadResult Ok(WorkspaceConfig config, IReadOnlyList<string>? warnings = null) =>
        new() { Success = true, Config = config, Warnings = warnings ?? [] };

    public static WorkspaceLoadResult Fail(string error) =>
        new() { Success = false, Error = error };
}

/// <summary>A resolved member with an absolute local filesystem path ready for analysis.</summary>
public sealed class ResolvedWorkspaceMember
{
    public required WorkspaceMember Definition { get; init; }
    public required string AbsolutePath { get; init; }
    public required bool IsRemote { get; init; }
    public string? CachePath { get; init; }
    public string? ResolvedBranch { get; init; }
    public string? HeadSha { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public string? Error { get; init; }

    /// <summary>True when resolve completed without error (path existence is validated at resolve time).</summary>
    public bool Success => string.IsNullOrEmpty(Error) && !string.IsNullOrWhiteSpace(AbsolutePath);
}

/// <summary>Health / freshness of a member's per-repo wiki.</summary>
public sealed class MemberWikiStatus
{
    public required string MemberId { get; init; }
    public required string WikiAbsolutePath { get; init; }
    public bool Exists { get; init; }
    public bool HasIndex { get; init; }
    public bool HasArchitecture { get; init; }
    public DateTimeOffset? LastWriteUtc { get; init; }
    public bool IsStale { get; init; }
    public string Summary { get; init; } = "";
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Cross-repo signals collected from members (file-based heuristics only).</summary>
public sealed class CrossRepoSignals
{
    public IReadOnlyList<PackageSignal> SharedPackages { get; init; } = [];
    public IReadOnlyList<ProjectReferenceSignal> ProjectReferences { get; init; } = [];
    public IReadOnlyList<OwnershipSignal> Ownership { get; init; } = [];
    public IReadOnlyList<ContractSignal> Contracts { get; init; } = [];
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>A NuGet / npm package seen across members.</summary>
public sealed class PackageSignal
{
    public required string PackageId { get; init; }
    public string Ecosystem { get; init; } = "nuget";
    public IReadOnlyList<string> MemberIds { get; init; } = [];
    public IReadOnlyList<string> Versions { get; init; } = [];
}

/// <summary>Project reference edge (typically within a member; may hint shared project names).</summary>
public sealed class ProjectReferenceSignal
{
    public required string FromMemberId { get; init; }
    public required string FromProject { get; init; }
    public required string ToReference { get; init; }
    public string? MatchedMemberId { get; set; }
}

/// <summary>CODEOWNERS or similar ownership hint.</summary>
public sealed class OwnershipSignal
{
    public required string MemberId { get; init; }
    public string SourcePath { get; init; } = "";
    public string Excerpt { get; init; } = "";
}

/// <summary>Contract / OpenAPI / message schema hint.</summary>
public sealed class ContractSignal
{
    public required string MemberId { get; init; }
    public required string RelativePath { get; init; }
    public string Kind { get; init; } = "openapi";
}

/// <summary>Per-member analysis snapshot used during workspace generation.</summary>
public sealed class WorkspaceMemberAnalysis
{
    public required ResolvedWorkspaceMember Resolved { get; init; }
    public MemberWikiStatus? WikiStatus { get; init; }
    public RepoAnalysisResult? Analysis { get; init; }
    public GenerationResult? MemberGenerateResult { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Aggregated workspace analysis for system wiki generation.</summary>
public sealed class WorkspaceAnalysisResult
{
    public required WorkspaceConfig Config { get; init; }
    public required IReadOnlyList<WorkspaceMemberAnalysis> Members { get; init; }
    public required CrossRepoSignals Signals { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public TimeSpan Duration { get; init; }
}

/// <summary>Parameters for a workspace generate/update run.</summary>
public sealed class WorkspaceGenerationRequest
{
    public required WorkspaceConfig Config { get; init; }
    public required string WorkspaceRoot { get; init; }
    public required string OutputPath { get; init; }
    public bool Force { get; init; }
    public bool DryRun { get; init; }
    public bool Incremental { get; init; }
    public string? ModelOverride { get; init; }
    public string? ProviderOverride { get; init; }
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
    public IProgress<string>? Progress { get; init; }
}

/// <summary>Outcome of a workspace generate/update run.</summary>
public sealed class WorkspaceGenerationResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public string? OutputPath { get; init; }
    public IReadOnlyList<string> FilesWritten { get; init; } = [];
    public IReadOnlyList<string> FilesWouldCreate { get; init; } = [];
    public IReadOnlyList<string> FilesWouldUpdate { get; init; } = [];
    public IReadOnlyList<string> FilesUnchanged { get; init; } = [];
    public IReadOnlyList<string> StepsCompleted { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public string? Error { get; init; }
    public string? CorrelationId { get; init; }
    public bool DryRun { get; init; }
    public bool UsedOfflineFallback { get; init; } = true;
    public TimeSpan Duration { get; init; }
    public int MemberCount { get; init; }
    public int MembersGenerated { get; init; }
    public WorkspaceAnalysisResult? Analysis { get; init; }

    public static WorkspaceGenerationResult Ok(
        string message,
        string outputPath,
        IReadOnlyList<string> filesWritten,
        TimeSpan duration,
        IReadOnlyList<string>? warnings = null,
        string? correlationId = null,
        bool dryRun = false,
        IReadOnlyList<string>? stepsCompleted = null,
        IReadOnlyList<string>? filesWouldCreate = null,
        IReadOnlyList<string>? filesWouldUpdate = null,
        IReadOnlyList<string>? filesUnchanged = null,
        int memberCount = 0,
        int membersGenerated = 0,
        WorkspaceAnalysisResult? analysis = null,
        bool usedOfflineFallback = true) =>
        new()
        {
            Success = true,
            Message = message,
            OutputPath = outputPath,
            FilesWritten = filesWritten,
            Duration = duration,
            Warnings = warnings ?? [],
            CorrelationId = correlationId,
            DryRun = dryRun,
            StepsCompleted = stepsCompleted ?? [],
            FilesWouldCreate = filesWouldCreate ?? [],
            FilesWouldUpdate = filesWouldUpdate ?? [],
            FilesUnchanged = filesUnchanged ?? [],
            MemberCount = memberCount,
            MembersGenerated = membersGenerated,
            Analysis = analysis,
            UsedOfflineFallback = usedOfflineFallback
        };

    public static WorkspaceGenerationResult Fail(
        string error,
        TimeSpan? duration = null,
        string? correlationId = null,
        IReadOnlyList<string>? warnings = null) =>
        new()
        {
            Success = false,
            Message = "Workspace generation failed.",
            Error = error,
            Duration = duration ?? TimeSpan.Zero,
            CorrelationId = correlationId,
            Warnings = warnings ?? []
        };
}

/// <summary>Persisted workspace last-run state for incremental updates.</summary>
public sealed class WorkspaceLastRunState
{
    public DateTimeOffset TimestampUtc { get; set; }
    public string? CorrelationId { get; set; }
    public string Mode { get; set; } = "generate";
    public string? OutputPath { get; set; }
    public string? WorkspaceName { get; set; }
    public IReadOnlyList<string> FilesWritten { get; set; } = [];
    public string? ToolVersion { get; set; }
    public Dictionary<string, WorkspaceMemberLastRun> Members { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Per-member snapshot inside workspace last-run state.</summary>
public sealed class WorkspaceMemberLastRun
{
    public string? HeadSha { get; set; }
    public DateTimeOffset? WikiLastWriteUtc { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public bool WikiExisted { get; set; }
}

/// <summary>Status snapshot for <c>workspace status</c>.</summary>
public sealed class WorkspaceStatusResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public WorkspaceConfig? Config { get; init; }
    public WorkspaceLastRunState? LastRun { get; init; }
    public IReadOnlyList<ResolvedWorkspaceMember> ResolvedMembers { get; init; } = [];
    public IReadOnlyList<MemberWikiStatus> WikiStatuses { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Result of scaffolding a workspace definition.</summary>
public sealed class WorkspaceInitResult
{
    public required bool Success { get; init; }
    public string Message { get; init; } = "";
    public string? ConfigPath { get; init; }
    public IReadOnlyList<string> FilesCreated { get; init; } = [];
    public string? Error { get; init; }

    public static WorkspaceInitResult Ok(string message, string configPath, IReadOnlyList<string> created) =>
        new() { Success = true, Message = message, ConfigPath = configPath, FilesCreated = created };

    public static WorkspaceInitResult Fail(string error) =>
        new() { Success = false, Message = "Workspace init failed.", Error = error };
}
