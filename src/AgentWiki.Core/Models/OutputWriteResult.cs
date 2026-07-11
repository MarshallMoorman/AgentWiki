namespace AgentWiki.Core.Models;

/// <summary>
/// Result of writing (or dry-run planning) wiki sections to disk.
/// </summary>
public sealed class OutputWriteResult
{
    /// <summary>All relative paths touched (written or planned).</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    /// <summary>Relative paths that would be created (do not exist yet). Dry-run only.</summary>
    public IReadOnlyList<string> WouldCreate { get; init; } = [];

    /// <summary>Relative paths that would be updated (exist and content differs). Dry-run only.</summary>
    public IReadOnlyList<string> WouldUpdate { get; init; } = [];

    /// <summary>Relative paths unchanged (exist and content matches). Dry-run only.</summary>
    public IReadOnlyList<string> Unchanged { get; init; } = [];

    public bool IsDryRun { get; init; }

    public int ChangeCount => WouldCreate.Count + WouldUpdate.Count;
}
