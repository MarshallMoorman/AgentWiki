using AgentWiki.Core.Generation;

namespace AgentWiki.Core.Models;

/// <summary>
/// Outcome of a wiki generation or update run.
/// </summary>
public sealed class GenerationResult
{
    /// <summary>Whether the run completed successfully.</summary>
    public required bool Success { get; init; }

    /// <summary>Human-readable summary of the run.</summary>
    public required string Message { get; init; }

    /// <summary>Absolute path to the wiki output directory.</summary>
    public string? OutputPath { get; init; }

    /// <summary>Markdown files written during this run (relative to output).</summary>
    public IReadOnlyList<string> FilesWritten { get; init; } = [];

    /// <summary>Optional repository analysis snapshot used for this run.</summary>
    public RepoAnalysisResult? Analysis { get; init; }

    /// <summary>Optional change-detection snapshot (incremental update mode).</summary>
    public ChangeDetectionResult? ChangeDetection { get; init; }

    /// <summary>Approximate input tokens consumed.</summary>
    public int InputTokens { get; init; }

    /// <summary>Approximate output tokens consumed.</summary>
    public int OutputTokens { get; init; }

    /// <summary>Rough cost estimate when tokens were consumed.</summary>
    public CostEstimate? CostEstimate { get; init; }

    /// <summary>Wall-clock duration of the run.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Non-fatal warnings collected during the run.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Fatal error message when <see cref="Success"/> is false.</summary>
    public string? Error { get; init; }

    /// <summary>Correlation id for this run (logs / support).</summary>
    public string? CorrelationId { get; init; }

    /// <summary>True when this was a dry-run (no files written).</summary>
    public bool DryRun { get; init; }

    /// <summary>Pipeline steps completed (from orchestrator).</summary>
    public IReadOnlyList<string> StepsCompleted { get; init; } = [];

    /// <summary>Dry-run: relative paths that would be newly created.</summary>
    public IReadOnlyList<string> FilesWouldCreate { get; init; } = [];

    /// <summary>Dry-run: relative paths that would be updated.</summary>
    public IReadOnlyList<string> FilesWouldUpdate { get; init; } = [];

    /// <summary>Dry-run: relative paths already matching planned content.</summary>
    public IReadOnlyList<string> FilesUnchanged { get; init; } = [];

    /// <summary>Module count from the wiki bundle (when available).</summary>
    public int ModuleCount { get; init; }

    /// <summary>Whether offline fallback was used for any pipeline step.</summary>
    public bool UsedOfflineFallback { get; init; }

    /// <summary>Creates a successful result.</summary>
    public static GenerationResult Ok(
        string message,
        string outputPath,
        IReadOnlyList<string> filesWritten,
        TimeSpan duration,
        IReadOnlyList<string>? warnings = null,
        RepoAnalysisResult? analysis = null,
        int inputTokens = 0,
        int outputTokens = 0,
        ChangeDetectionResult? changeDetection = null,
        CostEstimate? costEstimate = null,
        string? correlationId = null,
        bool dryRun = false,
        IReadOnlyList<string>? stepsCompleted = null,
        IReadOnlyList<string>? filesWouldCreate = null,
        IReadOnlyList<string>? filesWouldUpdate = null,
        IReadOnlyList<string>? filesUnchanged = null,
        int moduleCount = 0,
        bool usedOfflineFallback = false) =>
        new()
        {
            Success = true,
            Message = message,
            OutputPath = outputPath,
            FilesWritten = filesWritten,
            Duration = duration,
            Warnings = warnings ?? [],
            Analysis = analysis,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            ChangeDetection = changeDetection,
            CostEstimate = costEstimate,
            CorrelationId = correlationId,
            DryRun = dryRun,
            StepsCompleted = stepsCompleted ?? [],
            FilesWouldCreate = filesWouldCreate ?? [],
            FilesWouldUpdate = filesWouldUpdate ?? [],
            FilesUnchanged = filesUnchanged ?? [],
            ModuleCount = moduleCount,
            UsedOfflineFallback = usedOfflineFallback
        };

    /// <summary>Creates a failed result.</summary>
    public static GenerationResult Fail(
        string error,
        TimeSpan? duration = null,
        string? correlationId = null) =>
        new()
        {
            Success = false,
            Message = "Generation failed.",
            Error = error,
            Duration = duration ?? TimeSpan.Zero,
            CorrelationId = correlationId
        };
}
