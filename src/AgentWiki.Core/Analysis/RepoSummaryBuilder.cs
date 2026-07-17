using System.Text;
using AgentWiki.Core.Models;
using AgentWiki.Core;

namespace AgentWiki.Core.Analysis;

/// <summary>
/// Builds a concise, LLM-friendly textual summary of a repository inventory.
/// </summary>
public static class RepoSummaryBuilder
{
    /// <summary>Full inventory summary for wiki inventory pages.</summary>
    public static string Build(string repoName, string repoPath, RepoStats stats, IReadOnlyList<RepoFile> files) =>
        Build(
            repoName,
            repoPath,
            stats,
            files,
            maxSelectedFiles: Constants.Analysis.FullSummaryMaxSelectedFiles,
            maxChars: null);

    /// <summary>Compact summary suitable for LLM prompts (bounded size).</summary>
    public static string BuildForLlm(
        string repoName,
        string repoPath,
        RepoStats stats,
        IReadOnlyList<RepoFile> files,
        int maxChars = Constants.Config.MaxLlmSummaryChars) =>
        Build(
            repoName,
            repoPath,
            stats,
            files,
            maxSelectedFiles: Constants.Analysis.LlmSummaryMaxSelectedFiles,
            maxChars: maxChars);

    public static string Build(
        string repoName,
        string repoPath,
        RepoStats stats,
        IReadOnlyList<RepoFile> files,
        int maxSelectedFiles,
        int? maxChars)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Repository: {repoName}");
        // Never emit machine-specific absolute paths — wiki docs must be portable.
        sb.AppendLine($"Path: {PathUtility.RepoRootDisplayPath}");
        sb.AppendLine();
        sb.AppendLine("## Inventory summary");
        sb.AppendLine($"- Total files (after ignores): {stats.TotalFiles}");
        sb.AppendLine($"- Selected for analysis: {stats.SelectedFiles}");
        sb.AppendLine($"- Total size: {FormatSize(stats.TotalSizeBytes)}");
        sb.AppendLine($"- Approximate lines (text files): {stats.TotalLines:N0}");
        sb.AppendLine();

        sb.AppendLine("## Files by category");
        foreach (var category in Enum.GetValues<FileCategory>())
        {
            stats.FilesByCategory.TryGetValue(category, out var count);
            sb.AppendLine($"- {category}: {count}");
        }

        sb.AppendLine();
        sb.AppendLine("## Detected languages");
        if (stats.DetectedLanguages.Count == 0)
        {
            sb.AppendLine("- (none detected)");
        }
        else
        {
            foreach (var lang in stats.DetectedLanguages)
            {
                stats.FilesByLanguage.TryGetValue(lang, out var count);
                sb.AppendLine($"- {lang}: {count} file(s)");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Top folders");
        if (stats.TopFolders.Count == 0)
        {
            sb.AppendLine("- (root only)");
        }
        else
        {
            foreach (var folder in stats.TopFolders.Take(12))
            {
                sb.AppendLine($"- `{folder.RelativePath}/` — {folder.FileCount} files ({FormatSize(folder.SizeBytes)})");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Top extensions");
        foreach (var ext in stats.FilesByExtension.OrderByDescending(kv => kv.Value).Take(12))
        {
            var label = string.IsNullOrEmpty(ext.Key) ? "(no extension)" : ext.Key;
            sb.AppendLine($"- {label}: {ext.Value}");
        }

        var selected = files.Where(f => f.SelectedForAnalysis).Take(maxSelectedFiles).ToList();
        if (selected.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Selected files (sample for analysis)");
            foreach (var file in selected)
            {
                var lines = file.LineCount is int n ? $", ~{n} lines" : "";
                sb.AppendLine($"- `{file.RelativePath}` [{file.Category}{lines}]");
            }

            if (stats.SelectedFiles > selected.Count)
            {
                sb.AppendLine($"- … and {stats.SelectedFiles - selected.Count} more selected file(s)");
            }
        }

        var text = sb.ToString().TrimEnd();
        return maxChars is int limit ? PromptText.TruncateForLlm(text, limit) : text;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}
