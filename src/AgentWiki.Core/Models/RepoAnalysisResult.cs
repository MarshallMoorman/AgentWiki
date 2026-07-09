namespace AgentWiki.Core.Models;

/// <summary>
/// Full result of a repository analysis pass.
/// </summary>
public sealed class RepoAnalysisResult
{
    public required string RepoPath { get; init; }
    public required string RepoName { get; init; }
    public required IReadOnlyList<RepoFile> Files { get; init; }
    public required RepoStats Stats { get; init; }
    public required string Summary { get; init; }

    /// <summary>How files were discovered: Git, FileSystemWalk, or Hybrid.</summary>
    public required string DiscoveryMethod { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Aggregate statistics for a repository inventory.
/// </summary>
public sealed class RepoStats
{
    public int TotalFiles { get; init; }
    public int SelectedFiles { get; init; }
    public long TotalSizeBytes { get; init; }
    public int TotalLines { get; init; }
    public IReadOnlyDictionary<FileCategory, int> FilesByCategory { get; init; } =
        new Dictionary<FileCategory, int>();
    public IReadOnlyDictionary<string, int> FilesByExtension { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, int> FilesByLanguage { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<FolderStat> TopFolders { get; init; } = [];
    public IReadOnlyList<string> DetectedLanguages { get; init; } = [];
}

/// <summary>
/// File count / size for a top-level (or shallow) folder.
/// </summary>
public sealed record FolderStat(string RelativePath, int FileCount, long SizeBytes);
