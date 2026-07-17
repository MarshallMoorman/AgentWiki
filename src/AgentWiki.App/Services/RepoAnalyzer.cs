using System.Diagnostics;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;
using AgentWiki.Core;

namespace AgentWiki.App.Services;

/// <summary>
/// Walks a repository, respects <c>.gitignore</c> + config ignore patterns,
/// categorizes files, and produces inventory stats + an LLM-oriented summary.
/// </summary>
public sealed class RepoAnalyzer(ILogger<RepoAnalyzer> logger) : IRepoAnalyzer
{
    private static int AbsoluteFileCap => Constants.Analysis.AbsoluteFileCap;

    private static long DefaultMaxLineCountBytes => Constants.Analysis.DefaultMaxLineCountBytes;

    /// <inheritdoc />
    public async Task<RepoAnalysisResult> AnalyzeAsync(
        string repoPath,
        AgentWikiConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);
        ArgumentNullException.ThrowIfNull(config);

        var sw = Stopwatch.StartNew();
        var root = PathUtility.ExpandAndResolve(repoPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Repository path does not exist: {root}");
        }

        var repoName = new DirectoryInfo(root).Name;
        var warnings = new List<string>();
        var maxFiles = config.MaxFilesToAnalyze > 0
            ? config.MaxFilesToAnalyze
            : Constants.Config.MaxFilesToAnalyze;
        var maxLineCountBytes = DefaultMaxLineCountBytes;

        logger.LogInformation("Analyzing repository {RepoPath} (maxFiles={MaxFiles})", root, maxFiles);

        var matcher = new GitIgnoreMatcher(config.IgnorePatterns);
        // Root .gitignore always loaded so config + root rules apply even for git discovery re-filter.
        var rootGitIgnore = Path.Combine(root, ".gitignore");
        if (File.Exists(rootGitIgnore))
        {
            matcher.AddGitIgnoreFile(root, rootGitIgnore);
        }

        var discoveryMethod = "FileSystemWalk";
        List<string> relativePaths;

        if (await TryDiscoverViaGitAsync(root, cancellationToken).ConfigureAwait(false) is { Count: > 0 } gitPaths)
        {
            relativePaths = gitPaths;
            discoveryMethod = "Git";
            logger.LogDebug("Discovered {Count} paths via git ls-files", relativePaths.Count);
        }
        else
        {
            relativePaths = DiscoverViaFileSystem(root, matcher, warnings, cancellationToken);
            logger.LogDebug("Discovered {Count} paths via filesystem walk", relativePaths.Count);
        }

        // Apply matcher so config IgnorePatterns (and root gitignore) always filter the inventory.
        relativePaths = relativePaths
            .Where(p => !matcher.IsIgnored(p, isDirectory: false))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (relativePaths.Count > AbsoluteFileCap)
        {
            warnings.Add($"Repository has {relativePaths.Count} files; capped inventory at {AbsoluteFileCap}.");
            relativePaths = relativePaths.Take(AbsoluteFileCap).ToList();
        }

        var files = new List<RepoFile>(relativePaths.Count);
        foreach (var relative in relativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var absolute = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolute))
            {
                continue;
            }

            long size;
            try
            {
                size = new FileInfo(absolute).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Could not stat `{relative}`: {ex.Message}");
                continue;
            }

            var extension = Path.GetExtension(relative);
            var category = FileCategorizer.Categorize(relative);
            var language = FileCategorizer.DetectLanguage(extension);
            int? lineCount = null;

            if (!FileCategorizer.IsBinaryExtension(extension) && size > 0 && size <= maxLineCountBytes)
            {
                lineCount = await TryCountLinesAsync(absolute, cancellationToken).ConfigureAwait(false);
            }

            files.Add(new RepoFile
            {
                RelativePath = relative.Replace('\\', '/'),
                AbsolutePath = absolute,
                Category = category,
                SizeBytes = size,
                Extension = string.IsNullOrEmpty(extension) ? null : extension.ToLowerInvariant(),
                Language = language,
                LineCount = lineCount,
                SelectedForAnalysis = false
            });
        }

        var selectedPaths = SelectFilesForAnalysis(files, maxFiles);
        files = files
            .Select(f => f with { SelectedForAnalysis = selectedPaths.Contains(f.RelativePath) })
            .ToList();

        var stats = BuildStats(files);
        var summary = RepoSummaryBuilder.Build(repoName, root, stats, files);
        sw.Stop();

        logger.LogInformation(
            "Analysis complete for {RepoName}: {Total} files, {Selected} selected, {Lines} lines, method={Method}, duration={Duration}ms",
            repoName,
            stats.TotalFiles,
            stats.SelectedFiles,
            stats.TotalLines,
            discoveryMethod,
            sw.ElapsedMilliseconds);

        return new RepoAnalysisResult
        {
            RepoPath = root,
            RepoName = repoName,
            Files = files,
            Stats = stats,
            Summary = summary,
            DiscoveryMethod = discoveryMethod,
            Warnings = warnings,
            Duration = sw.Elapsed
        };
    }

    /// <summary>
    /// Depth-first walk that loads nested <c>.gitignore</c> files as directories are entered.
    /// </summary>
    private static List<string> DiscoverViaFileSystem(
        string root,
        GitIgnoreMatcher matcher,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var results = new List<string>();
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = stack.Pop();

            // Load nested .gitignore for this directory (root already loaded by caller).
            if (!PathsEqual(current, root))
            {
                var nestedIgnore = Path.Combine(current, ".gitignore");
                if (File.Exists(nestedIgnore))
                {
                    matcher.AddGitIgnoreFile(root, nestedIgnore);
                }
            }

            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var rel = Path.GetRelativePath(root, current).Replace('\\', '/');
                warnings.Add($"Could not enumerate `{rel}`: {ex.Message}");
                continue;
            }

            foreach (var entry in entries.OrderBy(e => e, StringComparer.OrdinalIgnoreCase))
            {
                string relative;
                bool isDir;
                try
                {
                    relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
                    isDir = (File.GetAttributes(entry) & FileAttributes.Directory) != 0;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (matcher.IsIgnored(relative, isDir))
                {
                    continue;
                }

                if (isDir)
                {
                    stack.Push(entry);
                    continue;
                }

                results.Add(relative);
                if (results.Count >= AbsoluteFileCap)
                {
                    warnings.Add($"Filesystem walk hit hard cap of {AbsoluteFileCap} files.");
                    return results;
                }
            }
        }

        return results;
    }

    private async Task<List<string>?> TryDiscoverViaGitAsync(string root, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Path.Combine(root, ".git")))
        {
            return null;
        }

        try
        {
            // Cached + others, excluding standard gitignore rules. -z for safe path parsing.
            var output = await RunGitAsync(
                    root,
                    ["ls-files", "-z", "-c", "-o", "--exclude-standard"],
                    cancellationToken)
                .ConfigureAwait(false);

            if (output is null)
            {
                return null;
            }

            var paths = output
                .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Replace('\\', '/'))
                .Where(p => p.Length > 0 && !p.EndsWith('/'))
                .ToList();

            return paths.Count == 0 ? null : paths;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "git ls-files failed; falling back to filesystem walk");
            return null;
        }
    }

    private static async Task<string?> RunGitAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
        {
            return null;
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            var stderr = await stderrTask.ConfigureAwait(false);
            throw new InvalidOperationException($"git exited {process.ExitCode}: {stderr.Trim()}");
        }

        return stdout;
    }

    private static async Task<int?> TryCountLinesAsync(string absolutePath, CancellationToken cancellationToken)
    {
        try
        {
            var count = 0;
            await using var stream = File.OpenRead(absolutePath);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                count++;
            }

            return count;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Prefer source → infrastructure (policies/pipelines) → tests → config → docs → diagrams → other;
    /// stable by path. Infrastructure files are boosted so APIM policies and build pipelines
    /// are included even when MaxFilesToAnalyze is tight.
    /// </summary>
    private static HashSet<string> SelectFilesForAnalysis(IReadOnlyList<RepoFile> files, int maxFiles)
    {
        static int Priority(RepoFile f)
        {
            // Boost deployment/infra artifacts above generic configuration.
            if (FileCategorizer.IsInfrastructurePath(f.RelativePath))
            {
                return 1;
            }

            return f.Category switch
            {
                FileCategory.SourceCode => 0,
                FileCategory.Tests => 2,
                FileCategory.Configuration => 3,
                FileCategory.Documentation => 4,
                FileCategory.Diagrams => 5,
                _ => 6
            };
        }

        return files
            .OrderBy(Priority)
            .ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(maxFiles)
            .Select(f => f.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static RepoStats BuildStats(IReadOnlyList<RepoFile> files)
    {
        var byCategory = files
            .GroupBy(f => f.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var category in Enum.GetValues<FileCategory>())
        {
            byCategory.TryAdd(category, 0);
        }

        var byExtension = files
            .GroupBy(f => f.Extension ?? "")
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var byLanguage = files
            .Where(f => !string.IsNullOrWhiteSpace(f.Language))
            .GroupBy(f => f.Language!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var topFolders = files
            .Select(f =>
            {
                var slash = f.RelativePath.IndexOf('/');
                var folder = slash < 0 ? "(root)" : f.RelativePath[..slash];
                return (folder, f);
            })
            .GroupBy(x => x.folder, StringComparer.OrdinalIgnoreCase)
            .Select(g => new FolderStat(
                g.Key,
                g.Count(),
                g.Sum(x => x.f.SizeBytes)))
            .OrderByDescending(f => f.FileCount)
            .ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        return new RepoStats
        {
            TotalFiles = files.Count,
            SelectedFiles = files.Count(f => f.SelectedForAnalysis),
            TotalSizeBytes = files.Sum(f => f.SizeBytes),
            TotalLines = files.Sum(f => f.LineCount ?? 0),
            FilesByCategory = byCategory,
            FilesByExtension = byExtension,
            FilesByLanguage = byLanguage,
            TopFolders = topFolders,
            DetectedLanguages = byLanguage
                .OrderByDescending(kv => kv.Value)
                .Select(kv => kv.Key)
                .ToList()
        };
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
