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

    /// <summary>Approximate input tokens consumed (0 until SK integration).</summary>
    public int InputTokens { get; init; }

    /// <summary>Approximate output tokens consumed (0 until SK integration).</summary>
    public int OutputTokens { get; init; }

    /// <summary>Wall-clock duration of the run.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Non-fatal warnings collected during the run.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Fatal error message when <see cref="Success"/> is false.</summary>
    public string? Error { get; init; }

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
        ChangeDetectionResult? changeDetection = null) =>
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
            ChangeDetection = changeDetection
        };

    /// <summary>Creates a failed result.</summary>
    public static GenerationResult Fail(string error, TimeSpan? duration = null) =>
        new()
        {
            Success = false,
            Message = "Generation failed.",
            Error = error,
            Duration = duration ?? TimeSpan.Zero
        };
}
