using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;
using AgentWiki.Core;

namespace AgentWiki.App.Services;

/// <summary>
/// Detects git changes since the last successful wiki run and maps them to wiki sections.
/// </summary>
public sealed class GitChangeDetector(
    ILastRunStore lastRunStore,
    ILogger<GitChangeDetector> logger) : IChangeDetector
{
    private static int FullRegenerationFileThreshold => Constants.Analysis.FullRegenerationFileThreshold;

    /// <inheritdoc />
    public async Task<ChangeDetectionResult> DetectAsync(
        string repoPath,
        AgentWikiConfig config,
        RepoAnalysisResult? analysis = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);
        ArgumentNullException.ThrowIfNull(config);

        var root = Path.GetFullPath(repoPath);
        var warnings = new List<string>();
        var currentSha = await GitProcess.TryGetHeadShaAsync(root, cancellationToken).ConfigureAwait(false);

        if (!config.EnableIncrementalUpdates)
        {
            return ChangeDetectionResult.Full(
                "Incremental updates disabled in configuration.",
                currentSha,
                warnings);
        }

        var lastRun = await lastRunStore.LoadAsync(root, cancellationToken).ConfigureAwait(false);
        if (lastRun is null || string.IsNullOrWhiteSpace(lastRun.CommitSha))
        {
            return ChangeDetectionResult.Full(
                "No last-run baseline found; performing full generation.",
                currentSha,
                warnings);
        }

        if (!GitProcess.IsGitRepository(root))
        {
            warnings.Add("Repository is not a git checkout; falling back to full regeneration.");
            return ChangeDetectionResult.Full(
                "Git repository not detected.",
                currentSha,
                warnings);
        }

        if (string.IsNullOrWhiteSpace(currentSha))
        {
            warnings.Add("Unable to resolve HEAD commit; falling back to full regeneration.");
            return ChangeDetectionResult.Full(
                "Could not resolve current commit SHA.",
                null,
                warnings);
        }

        if (string.Equals(lastRun.CommitSha, currentSha, StringComparison.OrdinalIgnoreCase))
        {
            // Still check for uncommitted changes against HEAD.
            var dirty = await GetChangedFilesAsync(root, lastRun.CommitSha, includeUncommitted: true, cancellationToken)
                .ConfigureAwait(false);
            dirty = FilterNoise(dirty, config);

            if (dirty.Count == 0)
            {
                logger.LogInformation(
                    "No changes detected since last run (commit={Commit})",
                    currentSha);
                return new ChangeDetectionResult
                {
                    HasBaseline = true,
                    RequiresFullRegeneration = false,
                    NoChanges = true,
                    BaselineCommitSha = lastRun.CommitSha,
                    CurrentCommitSha = currentSha,
                    Reason = $"No changes since last run at {lastRun.CommitSha[..Math.Min(7, lastRun.CommitSha.Length)]}.",
                    DetectionMethod = "git",
                    Warnings = warnings
                };
            }
        }

        List<string> changed;
        try
        {
            changed = await GetChangedFilesAsync(root, lastRun.CommitSha, includeUncommitted: true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            warnings.Add($"git diff failed ({ex.Message}); falling back to full regeneration.");
            return ChangeDetectionResult.Full(
                "git diff failed against last-run commit.",
                currentSha,
                warnings);
        }

        changed = FilterNoise(changed, config);

        if (changed.Count == 0)
        {
            return new ChangeDetectionResult
            {
                HasBaseline = true,
                RequiresFullRegeneration = false,
                NoChanges = true,
                BaselineCommitSha = lastRun.CommitSha,
                CurrentCommitSha = currentSha,
                Reason = "No relevant source changes since last run (wiki/tool noise filtered).",
                DetectionMethod = "git",
                Warnings = warnings
            };
        }

        if (changed.Count >= FullRegenerationFileThreshold)
        {
            return new ChangeDetectionResult
            {
                HasBaseline = true,
                RequiresFullRegeneration = true,
                NoChanges = false,
                BaselineCommitSha = lastRun.CommitSha,
                CurrentCommitSha = currentSha,
                ChangedFiles = changed,
                Reason = $"Large change set ({changed.Count} files) — full regeneration.",
                DetectionMethod = "git",
                Warnings = warnings
            };
        }

        var mapping = MapChangedFiles(changed, analysis, lastRun);
        logger.LogInformation(
            "Change detection: {Count} files, architecture={Arch}, modules={Modules}, crossCutting={Cross}",
            changed.Count,
            mapping.ArchitectureAffected,
            string.Join(',', mapping.AffectedModuleIds),
            string.Join(',', mapping.AffectedCrossCuttingIds));

        // If mapping found nothing specific, still regenerate architecture + support pages via "full modules" soft path.
        var requiresFull = mapping.AffectedModuleIds.Count == 0
                           && mapping.AffectedCrossCuttingIds.Count == 0
                           && !mapping.ArchitectureAffected;

        return new ChangeDetectionResult
        {
            HasBaseline = true,
            RequiresFullRegeneration = requiresFull,
            NoChanges = false,
            BaselineCommitSha = lastRun.CommitSha,
            CurrentCommitSha = currentSha,
            ChangedFiles = changed,
            AffectedModuleIds = mapping.AffectedModuleIds,
            AffectedCrossCuttingIds = mapping.AffectedCrossCuttingIds,
            ArchitectureAffected = mapping.ArchitectureAffected || requiresFull,
            Reason = requiresFull
                ? $"Detected {changed.Count} changed file(s); could not map to specific modules — full regeneration."
                : $"Detected {changed.Count} changed file(s) since {lastRun.CommitSha[..Math.Min(7, lastRun.CommitSha.Length)]}.",
            DetectionMethod = "git",
            Warnings = warnings
        };
    }

    private static async Task<List<string>> GetChangedFilesAsync(
        string repoPath,
        string baselineSha,
        bool includeUncommitted,
        CancellationToken cancellationToken)
    {
        // Commits since baseline (may be empty if baseline == HEAD).
        var committed = await GitProcess.RunAsync(
                repoPath,
                ["diff", "--name-only", $"{baselineSha}..HEAD"],
                cancellationToken)
            .ConfigureAwait(false);

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddLines(files, committed);

        if (includeUncommitted)
        {
            var unstaged = await GitProcess.RunAsync(
                    repoPath,
                    ["diff", "--name-only"],
                    cancellationToken)
                .ConfigureAwait(false);
            var staged = await GitProcess.RunAsync(
                    repoPath,
                    ["diff", "--name-only", "--cached"],
                    cancellationToken)
                .ConfigureAwait(false);
            // Untracked files (not ignored)
            var untracked = await GitProcess.RunAsync(
                    repoPath,
                    ["ls-files", "--others", "--exclude-standard"],
                    cancellationToken)
                .ConfigureAwait(false);

            AddLines(files, unstaged);
            AddLines(files, staged);
            AddLines(files, untracked);
        }

        return files
            .Select(f => f.Replace('\\', '/'))
            .Where(f => f.Length > 0)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddLines(HashSet<string> target, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        foreach (var line in raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            target.Add(line.Trim());
        }
    }

    private static List<string> FilterNoise(IEnumerable<string> files, AgentWikiConfig config)
    {
        var output = config.OutputPath.Replace('\\', '/').Trim('/');
        return files
            .Where(f =>
            {
                var path = f.Replace('\\', '/');
                if (path.StartsWith(".agentwiki/", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (path.StartsWith(output + "/", StringComparison.OrdinalIgnoreCase)
                    || path.Equals(output, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (path.Equals(Constants.Paths.DefaultAgentMdPath, StringComparison.OrdinalIgnoreCase)
                    || path.Equals("CLAUDE.md", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Ignore pure docs that are not source unless under src/tests
                if (path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase)
                    && !path.StartsWith("docs/wiki/", StringComparison.OrdinalIgnoreCase) is false)
                {
                    // docs/wiki already filtered; other docs still count lightly — include them
                }

                return true;
            })
            .ToList();
    }

    private static (
        bool ArchitectureAffected,
        HashSet<string> AffectedModuleIds,
        HashSet<string> AffectedCrossCuttingIds)
        MapChangedFiles(
            IReadOnlyList<string> changedFiles,
            RepoAnalysisResult? analysis,
            LastRunState lastRun)
    {
        var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cross = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var architecture = false;

        // Prefer module roots from last known module ids + analysis projects.
        var moduleRoots = BuildModuleRoots(analysis, lastRun);

        foreach (var file in changedFiles)
        {
            var path = file.Replace('\\', '/');

            if (IsArchitectureSignal(path))
            {
                architecture = true;
            }

            if (IsConfigSignal(path))
            {
                cross.Add("configuration");
                architecture = true;
            }

            if (IsLoggingSignal(path))
            {
                cross.Add("logging-and-telemetry");
            }

            if (IsErrorSignal(path))
            {
                cross.Add("error-handling");
            }

            if (IsTestSignal(path))
            {
                cross.Add("testing");
            }

            foreach (var (moduleId, roots) in moduleRoots)
            {
                if (roots.Any(root => PathMatchesRoot(path, root)))
                {
                    modules.Add(moduleId);
                }
            }
        }

        // If tests changed but no module matched, still mark testing.
        if (changedFiles.Any(IsTestSignal) && modules.Count == 0)
        {
            cross.Add("testing");
            architecture = true;
        }

        return (architecture, modules, cross);
    }

    private static Dictionary<string, List<string>> BuildModuleRoots(
        RepoAnalysisResult? analysis,
        LastRunState lastRun)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (analysis is not null)
        {
            var projects = analysis.Files
                .Where(f => f.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                            || f.RelativePath.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
                            || f.RelativePath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase));

            foreach (var project in projects)
            {
                var id = Slug(Path.GetFileNameWithoutExtension(project.RelativePath));
                var dir = Path.GetDirectoryName(project.RelativePath)?.Replace('\\', '/') ?? "";
                var root = string.IsNullOrEmpty(dir) ? "" : dir + "/";
                if (!map.TryGetValue(id, out var roots))
                {
                    roots = [];
                    map[id] = roots;
                }

                if (!string.IsNullOrEmpty(root))
                {
                    roots.Add(root);
                }
            }
        }

        // Ensure last-run module ids exist even if roots unknown (match by id fragment).
        foreach (var moduleId in lastRun.ModuleIds)
        {
            map.TryAdd(moduleId, []);
        }

        // Common defaults for this solution layout.
        map.TryAdd("agentwiki-cli", ["src/AgentWiki.Cli/"]);
        map.TryAdd("agentwiki-core", ["src/AgentWiki.Core/"]);
        map.TryAdd("agentwiki-cli-tests", ["tests/AgentWiki.Cli.Tests/"]);

        return map;
    }

    private static bool PathMatchesRoot(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        var normalized = root.Replace('\\', '/');
        if (!normalized.EndsWith('/'))
        {
            normalized += "/";
        }

        return path.StartsWith(normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsArchitectureSignal(string path) =>
        path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
        || path.Equals("Directory.Build.props", StringComparison.OrdinalIgnoreCase)
        || path.Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/Abstractions/", StringComparison.OrdinalIgnoreCase);

    private static bool IsConfigSignal(string path) =>
        path.Contains("appsettings", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
        || path.Contains("Config", StringComparison.OrdinalIgnoreCase);

    private static bool IsLoggingSignal(string path) =>
        path.Contains("Log", StringComparison.OrdinalIgnoreCase)
        || path.Contains("Serilog", StringComparison.OrdinalIgnoreCase)
        || path.Contains("Telemetry", StringComparison.OrdinalIgnoreCase);

    private static bool IsErrorSignal(string path) =>
        path.Contains("Exception", StringComparison.OrdinalIgnoreCase)
        || path.Contains("Error", StringComparison.OrdinalIgnoreCase)
        || path.Contains("Result", StringComparison.OrdinalIgnoreCase);

    private static bool IsTestSignal(string path) =>
        path.Contains("/tests/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("tests/", StringComparison.OrdinalIgnoreCase)
        || path.Contains(".Tests/", StringComparison.OrdinalIgnoreCase)
        || path.Contains("Test", StringComparison.OrdinalIgnoreCase);

    private static string Slug(string value)
    {
        var chars = value.Trim().ToLowerInvariant().Select(c =>
            char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Trim('-');
    }
}
