namespace AgentWiki.Core.Models;

/// <summary>
/// Lightweight inventory entry for a file discovered during repository analysis.
/// </summary>
public sealed record RepoFile
{
    public required string RelativePath { get; init; }
    public required string AbsolutePath { get; init; }
    public required FileCategory Category { get; init; }
    public required long SizeBytes { get; init; }
    public string? Extension { get; init; }
    public string? Language { get; init; }

    /// <summary>
    /// Approximate line count for text files; null when not counted (binary/large/skipped).
    /// </summary>
    public int? LineCount { get; init; }

    /// <summary>True when this file is included in the analysis subset sent to the LLM.</summary>
    public bool SelectedForAnalysis { get; init; }
}

/// <summary>
/// High-level classification of a repository file.
/// </summary>
public enum FileCategory
{
    SourceCode,
    Documentation,
    Configuration,
    Tests,
    Diagrams,
    Other
}
