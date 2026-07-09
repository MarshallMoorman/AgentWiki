namespace AgentWiki.Core.Models;

/// <summary>
/// Persisted state from the last successful wiki generation/update.
/// Stored at <c>.agentwiki/last-run.json</c>.
/// </summary>
public sealed class LastRunState
{
    public string? CommitSha { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public string? CorrelationId { get; set; }
    public string Mode { get; set; } = "generate";
    public string? OutputPath { get; set; }
    public IReadOnlyList<string> FilesWritten { get; set; } = [];
    public IReadOnlyList<string> ModuleIds { get; set; } = [];
    public int TotalFiles { get; set; }
    public string? ToolVersion { get; set; }
}

/// <summary>
/// Result of detecting repository changes since the last successful wiki run.
/// </summary>
public sealed class ChangeDetectionResult
{
    /// <summary>True when a last-run baseline was found.</summary>
    public required bool HasBaseline { get; init; }

    /// <summary>True when the whole wiki should be regenerated.</summary>
    public required bool RequiresFullRegeneration { get; init; }

    /// <summary>True when no source changes were detected since the baseline.</summary>
    public required bool NoChanges { get; init; }

    public string? BaselineCommitSha { get; init; }
    public string? CurrentCommitSha { get; init; }
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];
    public IReadOnlySet<string> AffectedModuleIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> AffectedCrossCuttingIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public bool ArchitectureAffected { get; init; }
    public string Reason { get; init; } = "";
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public string DetectionMethod { get; init; } = "none";

    public static ChangeDetectionResult Full(string reason, string? currentSha = null, IReadOnlyList<string>? warnings = null) =>
        new()
        {
            HasBaseline = false,
            RequiresFullRegeneration = true,
            NoChanges = false,
            CurrentCommitSha = currentSha,
            Reason = reason,
            Warnings = warnings ?? [],
            DetectionMethod = "full"
        };
}

/// <summary>
/// Scope of sections to regenerate during an incremental update.
/// </summary>
public sealed class IncrementalScope
{
    public bool IsFull { get; init; }
    public bool Architecture { get; init; } = true;
    public bool AllModules { get; init; } = true;
    public IReadOnlySet<string> ModuleIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public bool AllCrossCutting { get; init; } = true;
    public IReadOnlySet<string> CrossCuttingIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static IncrementalScope Full() => new()
    {
        IsFull = true,
        Architecture = true,
        AllModules = true,
        AllCrossCutting = true
    };

    public static IncrementalScope FromChanges(ChangeDetectionResult changes) =>
        changes.RequiresFullRegeneration
            ? Full()
            : new IncrementalScope
            {
                IsFull = false,
                Architecture = changes.ArchitectureAffected,
                AllModules = false,
                ModuleIds = changes.AffectedModuleIds,
                AllCrossCutting = false,
                CrossCuttingIds = changes.AffectedCrossCuttingIds
            };
}
