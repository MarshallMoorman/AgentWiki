using System.Text.RegularExpressions;
using AgentWiki.Core.Models;
using AgentWiki.Core;

namespace AgentWiki.Core.Generation;

/// <summary>
/// Heuristic module identification from inventory, solution/project structure, and config
/// (no LLM). Prefer .sln/.csproj roots, config-driven roots/globs, then folder fallbacks.
/// </summary>
public static partial class OfflineModulePlanner
{
    /// <summary>Plan modules using default config limits.</summary>
    public static ModulePlan Plan(RepoAnalysisResult analysis) =>
        Plan(analysis, new AgentWikiConfig());

    /// <summary>Plan modules using config limits, roots, and globs.</summary>
    public static ModulePlan Plan(RepoAnalysisResult analysis, AgentWikiConfig config)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(config);

        var maxModules = config.MaxModules > 0 ? config.MaxModules : Constants.Config.MaxModules;
        var maxFiles = config.MaxFilesPerModule > 0
            ? config.MaxFilesPerModule
            : Constants.Config.MaxFilesPerModule;

        var candidates = new List<ModuleCandidate>();

        // 1) Explicit config roots (highest priority)
        foreach (var root in config.ModuleRoots.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            candidates.Add(FromRootPath(analysis, root.Trim().Replace('\\', '/'), source: "config-root", kind: "configured", priority: 0));
        }

        // 2) Config globs
        foreach (var glob in config.ModuleGlobs.Where(g => !string.IsNullOrWhiteSpace(g)))
        {
            foreach (var root in ExpandModuleGlob(analysis, glob.Trim()))
            {
                candidates.Add(FromRootPath(analysis, root, source: "config-glob", kind: InferKindFromPath(root), priority: 1));
            }
        }

        // 3) Solution file project entries
        var slnProjects = DiscoverProjectsFromSolutions(analysis);
        foreach (var proj in slnProjects)
        {
            candidates.Add(FromProjectFile(analysis, proj.RelativePath, proj.Name, source: "sln", priority: 2));
        }

        // 4) Standalone project files not already covered
        var projectFiles = analysis.Files
            .Where(f => f.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                        || f.RelativePath.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
                        || f.RelativePath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var project in projectFiles)
        {
            if (candidates.Any(c => CoversPath(c, project.RelativePath)))
            {
                continue;
            }

            candidates.Add(FromProjectFile(
                analysis,
                project.RelativePath,
                Path.GetFileNameWithoutExtension(project.RelativePath),
                source: "csproj",
                priority: 3));
        }

        // 5) Static analysis project rollup (if present and still thin)
        if (analysis.StaticAnalysis is { Projects.Count: > 0 } staticAnalysis)
        {
            foreach (var project in staticAnalysis.Projects)
            {
                if (string.IsNullOrWhiteSpace(project.RelativePath)
                    || project.RelativePath is "." or "(repository)")
                {
                    continue;
                }

                if (candidates.Any(c =>
                        c.Name.Equals(project.Name, StringComparison.OrdinalIgnoreCase)
                        || CoversPath(c, project.RelativePath)))
                {
                    // Enrich kind when we already have the project
                    var existing = candidates.FirstOrDefault(c =>
                        c.Name.Equals(project.Name, StringComparison.OrdinalIgnoreCase));
                    if (existing is not null && existing.Kind is "library" or "unknown")
                    {
                        existing.Kind = project.Kind;
                    }

                    continue;
                }

                var root = project.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                           || project.RelativePath.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
                           || project.RelativePath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)
                    ? (Path.GetDirectoryName(project.RelativePath)?.Replace('\\', '/') ?? ".")
                    : project.RelativePath.TrimEnd('/');

                candidates.Add(FromRootPath(analysis, root, source: "static-analysis", kind: project.Kind, priority: 4, nameOverride: project.Name));
            }
        }

        // 6) Top-level folders when still empty
        if (candidates.Count == 0)
        {
            foreach (var folder in analysis.Stats.TopFolders
                         .Where(f => f.RelativePath is not "(root)")
                         .OrderByDescending(f => f.FileCount))
            {
                candidates.Add(FromRootPath(
                    analysis,
                    folder.RelativePath,
                    source: "folder",
                    kind: InferKindFromPath(folder.RelativePath),
                    priority: 5));
            }
        }

        // 7) Whole-repo fallback
        if (candidates.Count == 0)
        {
            candidates.Add(new ModuleCandidate
            {
                Name = analysis.RepoName,
                PreferredId = "repository",
                RootPaths = ["."],
                Kind = "repository",
                Source = "fallback",
                Priority = 9,
                Summary = "Entire repository treated as a single module.",
                RelatedFiles = analysis.Files
                    .Where(f => f.SelectedForAnalysis)
                    .Select(f => f.RelativePath)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .Take(maxFiles)
                    .ToList()
            });
        }

        // Deduplicate overlapping roots (prefer higher-priority / more specific)
        candidates = DeduplicateCandidates(candidates);

        // Sort: priority, then kind rank, then name
        var ordered = candidates
            .OrderBy(c => TestPenalty(c, config.IncludeTestProjectsAsModules))
            .ThenBy(c => c.Priority)
            .ThenBy(c => KindRank(c.Kind))
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxModules)
            .ToList();

        var modules = new List<ModuleDescriptor>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in ordered)
        {
            var related = candidate.RelatedFiles.Count > 0
                ? candidate.RelatedFiles
                : CollectRelatedFiles(analysis, candidate.RootPaths, maxFiles);

            // Enrich related files once more for completeness
            related = related
                .Concat(CollectRelatedFiles(analysis, candidate.RootPaths, maxFiles))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Take(maxFiles)
                .ToList();

            var id = EnsureUniqueId(BuildStableId(candidate), seenIds);
            var title = BuildTitle(candidate);
            var summary = string.IsNullOrWhiteSpace(candidate.Summary)
                ? BuildSummary(candidate, related.Count)
                : candidate.Summary;

            modules.Add(new ModuleDescriptor
            {
                Id = id,
                Name = title,
                Summary = summary,
                RootPaths = candidate.RootPaths
                    .Select(NormalizeRoot)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                RelatedFiles = related
            });
        }

        return new ModulePlan
        {
            Modules = modules,
            UsedOfflineFallback = true
        };
    }

    public static ModuleDocument BuildModuleDocument(ModuleDescriptor descriptor, RepoAnalysisResult analysis) =>
        BuildModuleDocument(descriptor, analysis, new AgentWikiConfig());

    public static ModuleDocument BuildModuleDocument(
        ModuleDescriptor descriptor,
        RepoAnalysisResult analysis,
        AgentWikiConfig config)
    {
        var maxFiles = config.MaxFilesPerModule > 0 ? config.MaxFilesPerModule : Constants.Config.MaxFilesPerModule;

        var sourceFiles = descriptor.RelatedFiles
            .Where(p => analysis.Files.Any(f =>
                f.RelativePath.Equals(p, StringComparison.OrdinalIgnoreCase)
                && f.Category is FileCategory.SourceCode or FileCategory.Configuration))
            .ToList();

        var staticAnalysis = analysis.StaticAnalysis;
        var moduleTypes = FilterTypesForModule(descriptor, staticAnalysis);
        var moduleEndpoints = FilterEndpointsForModule(descriptor, staticAnalysis);
        var moduleEntry = FilterEntryPointsForModule(descriptor, staticAnalysis, sourceFiles);
        var moduleDi = FilterDiForModule(descriptor, staticAnalysis, moduleTypes);
        var moduleObsolete = FilterObsoleteForModule(descriptor, staticAnalysis, moduleTypes);

        var purpose = string.IsNullOrWhiteSpace(descriptor.Summary)
            ? $"Module `{descriptor.Name}` inferred from repository inventory."
            : descriptor.Summary;
        if (moduleTypes.Count > 0)
        {
            purpose +=
                $" Static analysis found {moduleTypes.Count} public type(s)"
                + (moduleEndpoints.Count > 0 ? $" and {moduleEndpoints.Count} endpoint(s)" : "")
                + ".";
        }

        var keyTypes = moduleTypes.Count > 0
            ? moduleTypes
                .Select(t =>
                {
                    var ns = string.IsNullOrWhiteSpace(t.Namespace) ? "" : $"{t.Namespace}.";
                    var attrs = t.Attributes.Count > 0 ? $" [{string.Join(", ", t.Attributes.Take(3))}]" : "";
                    return $"{t.Kind} {ns}{t.Name}{attrs} (`{t.RelativePath}`)";
                })
                .Take(15)
                .ToList()
            : sourceFiles
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(15)
                .ToList();

        var howToExtend = new List<string>
        {
            $"Add new types under {string.Join(", ", descriptor.RootPaths.Select(p => $"`{p}`"))}.",
            "Keep public surface area documented in this module page when behavior changes.",
            "Prefer existing abstractions/interfaces before introducing new layers."
        };
        if (moduleTypes.Any(t => t.Kind == "interface"))
        {
            howToExtend.Insert(0,
                "Implement or extend existing public interfaces in this module rather than introducing parallel abstractions.");
        }

        if (moduleDi.Count > 0)
        {
            howToExtend.Add(
                "DI registration hints: " + string.Join(", ", moduleDi.Take(6).Select(d => $"`{d}`")) + ".");
        }

        var gotchas = new List<string>
        {
            staticAnalysis is { UsedRoslyn: true }
                ? "This module page was enriched with Roslyn syntax analysis; still verify behavior against source."
                : "This module page was generated offline from file inventory; verify responsibilities against source.",
            "Related file lists may be capped; inspect the project folder for the full set."
        };
        if (moduleObsolete.Count > 0)
        {
            gotchas.Add(
                "Obsolete symbols in this module: "
                + string.Join(", ", moduleObsolete.Take(5).Select(s => $"`{s}`"))
                + ".");
        }

        return new ModuleDocument
        {
            Id = descriptor.Id,
            Title = descriptor.Name,
            Purpose = purpose,
            EntryPoints = moduleEntry.Count > 0
                ? moduleEntry.Take(10).ToList()
                : sourceFiles
                    .Where(p => p.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase)
                                || p.Contains("/Commands/", StringComparison.OrdinalIgnoreCase)
                                || p.EndsWith("Startup.cs", StringComparison.OrdinalIgnoreCase))
                    .Take(10)
                    .ToList(),
            Dependencies = descriptor.RootPaths,
            KeyTypes = keyTypes.Take(20).ToList(),
            HowToExtend = howToExtend,
            Gotchas = gotchas,
            RelatedFiles = descriptor.RelatedFiles.Take(maxFiles).ToList(),
            Endpoints = moduleEndpoints.ToList(),
            UsedOfflineFallback = true
        };
    }

    public static IReadOnlyList<CrossCuttingDocument> BuildCrossCutting(RepoAnalysisResult analysis)
    {
        var docs = new List<CrossCuttingDocument>();

        docs.Add(BuildConcern(
            analysis,
            id: "configuration",
            title: "Configuration",
            match: f => f.Category == FileCategory.Configuration
                        || f.RelativePath.Contains("appsettings", StringComparison.OrdinalIgnoreCase)
                        || f.RelativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                        || f.RelativePath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
                        || f.RelativePath.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase),
            summary: "Configuration, project settings, Policies/ (APIM), and pipeline definitions from the inventory.",
            patterns:
            [
                "Prefer environment-specific overrides over hard-coded values.",
                "Keep secrets out of committed config; use env vars or secret stores.",
                "Treat Policies/ XML and azure-*-pipeline YAML as part of the deployment surface, not optional noise."
            ],
            guidance:
            [
                "Update `.agentwiki/config.json` for AgentWiki settings.",
                "Document new config keys next to the code that consumes them.",
                "When changing public APIs, check Policies/ and pipeline YAML for required deploy updates."
            ]));

        docs.Add(BuildConcern(
            analysis,
            id: "logging-and-telemetry",
            title: "Logging and Telemetry",
            match: f => f.RelativePath.Contains("Log", StringComparison.OrdinalIgnoreCase)
                        || f.RelativePath.Contains("Serilog", StringComparison.OrdinalIgnoreCase)
                        || f.RelativePath.Contains("Telemetry", StringComparison.OrdinalIgnoreCase)
                        || f.RelativePath.Contains("OpenTelemetry", StringComparison.OrdinalIgnoreCase),
            summary: "Logging/telemetry-related files and conventions inferred from inventory.",
            patterns:
            [
                "Use structured logging with correlation IDs for multi-step runs.",
                "Never log secrets, API keys, or full prompt/response payloads by default."
            ],
            guidance:
            [
                "Add log events around external calls and generation pipeline steps.",
                "Prefer warning/error levels for actionable failures."
            ]));

        docs.Add(BuildConcern(
            analysis,
            id: "error-handling",
            title: "Error Handling",
            match: f => f.RelativePath.Contains("Exception", StringComparison.OrdinalIgnoreCase)
                        || f.RelativePath.Contains("Error", StringComparison.OrdinalIgnoreCase)
                        || f.RelativePath.Contains("Result", StringComparison.OrdinalIgnoreCase),
            summary: "Error-handling patterns inferred from naming and result types in the inventory.",
            patterns:
            [
                "Return structured results from long-running operations when possible.",
                "Fail fast on invalid configuration; fall back gracefully for optional LLM features."
            ],
            guidance:
            [
                "Surface user-friendly CLI errors via Spectre while logging exception details.",
                "Preserve OperationCanceledException without wrapping."
            ]));

        docs.Add(BuildConcern(
            analysis,
            id: "testing",
            title: "Testing",
            match: f => f.Category == FileCategory.Tests,
            summary: "Test projects and files discovered during analysis.",
            patterns:
            [
                "Keep unit tests close to behavior; prefer deterministic offline fixtures for LLM paths.",
                "Use temp directories for filesystem-facing tests and clean up afterward."
            ],
            guidance:
            [
                "Add tests for new orchestrator steps and bootstrap edge cases.",
                "Mock ILlmCompletionService rather than calling live models in CI."
            ]));

        return docs;
    }

    private static ModuleCandidate FromProjectFile(
        RepoAnalysisResult analysis,
        string projectRelativePath,
        string name,
        string source,
        int priority)
    {
        var normalized = projectRelativePath.Replace('\\', '/');
        var dir = Path.GetDirectoryName(normalized)?.Replace('\\', '/') ?? "";
        var root = string.IsNullOrEmpty(dir) ? "." : dir;
        var kind = InferKindFromPath(normalized);
        // Peek at csproj content when absolute path exists for Sdk hints.
        kind = RefineKindFromProjectFile(analysis.RepoPath, normalized, kind);

        return new ModuleCandidate
        {
            Name = name,
            PreferredId = Slug(name),
            RootPaths = [NormalizeRoot(root)],
            Kind = kind,
            Source = source,
            Priority = priority,
            ProjectFile = normalized,
            Summary = $"{KindLabel(kind)} project `{name}` (`{normalized}`).",
            RelatedFiles = CollectRelatedFiles(analysis, [NormalizeRoot(root)], Constants.Config.MaxFilesPerModule)
        };
    }

    private static ModuleCandidate FromRootPath(
        RepoAnalysisResult analysis,
        string root,
        string source,
        string kind,
        int priority,
        string? nameOverride = null)
    {
        var normalized = NormalizeRoot(root).TrimEnd('/');
        if (string.IsNullOrEmpty(normalized))
        {
            normalized = ".";
        }

        var name = nameOverride
                   ?? (normalized == "."
                       ? analysis.RepoName
                       : normalized.Split('/').LastOrDefault() ?? normalized);

        return new ModuleCandidate
        {
            Name = name,
            PreferredId = BuildPathBasedId(normalized, name),
            RootPaths = [NormalizeRoot(normalized)],
            Kind = string.IsNullOrWhiteSpace(kind) ? InferKindFromPath(normalized) : kind,
            Source = source,
            Priority = priority,
            Summary = $"Module root `{NormalizeRoot(normalized)}` ({source}).",
            RelatedFiles = CollectRelatedFiles(analysis, [NormalizeRoot(normalized)], Constants.Config.MaxFilesPerModule)
        };
    }

    private static List<string> ExpandModuleGlob(RepoAnalysisResult analysis, string glob)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pattern = glob.Replace('\\', '/').Trim();

        // Directory-style: src/*/ or src/*
        if (pattern.EndsWith("/*", StringComparison.Ordinal) || pattern.EndsWith("/*/", StringComparison.Ordinal))
        {
            var prefix = pattern.TrimEnd('/').TrimEnd('*').TrimEnd('/');
            foreach (var file in analysis.Files)
            {
                var path = file.RelativePath.Replace('\\', '/');
                if (!path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase) && prefix.Length > 0)
                {
                    continue;
                }

                var rest = prefix.Length == 0 ? path : path[(prefix.Length + 1)..];
                var segment = rest.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (segment is null)
                {
                    continue;
                }

                roots.Add(string.IsNullOrEmpty(prefix) ? segment : prefix + "/" + segment);
            }

            return roots.OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Match project files or directories against simple * glob
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal) + "$";
        foreach (var file in analysis.Files)
        {
            var path = file.RelativePath.Replace('\\', '/');
            var dir = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
            if (Regex.IsMatch(path, regex, RegexOptions.IgnoreCase)
                || Regex.IsMatch(dir + "/", regex, RegexOptions.IgnoreCase)
                || Regex.IsMatch(dir, regex, RegexOptions.IgnoreCase))
            {
                if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
                {
                    roots.Add(string.IsNullOrEmpty(dir) ? "." : dir);
                }
                else if (!string.IsNullOrEmpty(dir))
                {
                    roots.Add(dir);
                }
            }
        }

        return roots.OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<(string Name, string RelativePath)> DiscoverProjectsFromSolutions(RepoAnalysisResult analysis)
    {
        var results = new List<(string Name, string RelativePath)>();
        var slnFiles = analysis.Files
            .Where(f => f.RelativePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                        || f.RelativePath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var sln in slnFiles)
        {
            var absolute = !string.IsNullOrWhiteSpace(sln.AbsolutePath) && File.Exists(sln.AbsolutePath)
                ? sln.AbsolutePath
                : Path.Combine(analysis.RepoPath, sln.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolute))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(absolute);
            }
            catch
            {
                continue;
            }

            var slnDir = Path.GetDirectoryName(sln.RelativePath)?.Replace('\\', '/') ?? "";
            foreach (Match match in SlnProjectRegex().Matches(text))
            {
                var typeGuid = match.Groups["type"].Value;
                // Solution folder GUID
                if (typeGuid.Equals("2150E333-8FDC-42A3-9474-1A3956D46DE8", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = match.Groups["name"].Value.Trim();
                var path = match.Groups["path"].Value.Replace('\\', '/').Trim();
                if (string.IsNullOrWhiteSpace(path)
                    || !(path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                         || path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
                         || path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)
                         || path.EndsWith(".vcxproj", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var combined = string.IsNullOrEmpty(slnDir) ? path : slnDir + "/" + path;
                // Normalize ./ and duplicate segments lightly
                combined = combined.Replace("//", "/", StringComparison.Ordinal);
                while (combined.Contains("/./", StringComparison.Ordinal))
                {
                    combined = combined.Replace("/./", "/", StringComparison.Ordinal);
                }

                results.Add((name, combined));
            }
        }

        return results
            .GroupBy(p => p.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static string RefineKindFromProjectFile(string repoRoot, string relativePath, string fallback)
    {
        var absolute = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolute))
        {
            return fallback;
        }

        try
        {
            var text = File.ReadAllText(absolute);
            if (text.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Microsoft.NET.Sdk.Razor", StringComparison.OrdinalIgnoreCase))
            {
                return "web";
            }

            if (text.Contains("Microsoft.NET.Sdk.Worker", StringComparison.OrdinalIgnoreCase))
            {
                return "worker";
            }

            if (text.Contains("AzureFunctionsVersion", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Microsoft.NET.Sdk.Functions", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Worker.Extensions.Http", StringComparison.OrdinalIgnoreCase))
            {
                return "function";
            }

            if (text.Contains("Microsoft.NET.Sdk.BlazorWebAssembly", StringComparison.OrdinalIgnoreCase))
            {
                return "web";
            }

            if (text.Contains("OutputType>Exe", StringComparison.OrdinalIgnoreCase)
                || text.Contains("OutputType>WinExe", StringComparison.OrdinalIgnoreCase))
            {
                return InferKindFromPath(relativePath) is "test" ? "test" : "console";
            }

            if (text.Contains("IsTestProject>true", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase))
            {
                return "test";
            }
        }
        catch
        {
            // ignore
        }

        return fallback;
    }

    private static List<string> CollectRelatedFiles(
        RepoAnalysisResult analysis,
        IReadOnlyList<string> rootPaths,
        int maxFiles)
    {
        var set = new List<string>();
        foreach (var root in rootPaths)
        {
            var normalized = NormalizeRoot(root);
            if (normalized is "." or "./" or "/")
            {
                set.AddRange(analysis.Files
                    .Where(f => f.SelectedForAnalysis)
                    .Select(f => f.RelativePath)
                    .Take(maxFiles));
                continue;
            }

            var prefix = normalized.TrimEnd('/') + "/";
            var rootFile = normalized.TrimEnd('/');
            set.AddRange(analysis.Files
                .Where(f =>
                    f.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || f.RelativePath.Equals(rootFile, StringComparison.OrdinalIgnoreCase)
                    || f.RelativePath.Equals(rootFile + ".csproj", StringComparison.OrdinalIgnoreCase))
                .Select(f => f.RelativePath));
        }

        // Prefer source/config first
        return set
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p =>
            {
                var f = analysis.Files.FirstOrDefault(x =>
                    x.RelativePath.Equals(p, StringComparison.OrdinalIgnoreCase));
                if (f is null)
                {
                    return 5;
                }

                return f.Category switch
                {
                    FileCategory.SourceCode => 0,
                    FileCategory.Configuration => 1,
                    FileCategory.Tests => 3,
                    FileCategory.Documentation => 4,
                    _ => 2
                };
            })
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Take(maxFiles)
            .ToList();
    }

    private static List<ModuleCandidate> DeduplicateCandidates(List<ModuleCandidate> candidates)
    {
        // Prefer more specific roots and better priority
        var sorted = candidates
            .OrderBy(c => c.Priority)
            .ThenByDescending(c => c.RootPaths.Max(r => r.Length))
            .ToList();

        var kept = new List<ModuleCandidate>();
        foreach (var candidate in sorted)
        {
            var duplicate = kept.FirstOrDefault(k =>
                RootsOverlap(k, candidate)
                || (k.ProjectFile is not null
                    && candidate.ProjectFile is not null
                    && k.ProjectFile.Equals(candidate.ProjectFile, StringComparison.OrdinalIgnoreCase))
                || k.Name.Equals(candidate.Name, StringComparison.OrdinalIgnoreCase)
                   && RootsOverlap(k, candidate));

            if (duplicate is null)
            {
                kept.Add(candidate);
                continue;
            }

            // Merge related files into the kept candidate
            duplicate.RelatedFiles = duplicate.RelatedFiles
                .Concat(candidate.RelatedFiles)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (duplicate.Kind is "library" or "unknown" && candidate.Kind is not ("library" or "unknown"))
            {
                duplicate.Kind = candidate.Kind;
            }
        }

        return kept;
    }

    private static bool RootsOverlap(ModuleCandidate a, ModuleCandidate b)
    {
        foreach (var ra in a.RootPaths)
        {
            foreach (var rb in b.RootPaths)
            {
                var na = NormalizeRoot(ra).TrimEnd('/');
                var nb = NormalizeRoot(rb).TrimEnd('/');
                if (na.Equals(nb, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (na != "." && nb != "."
                    && (nb.StartsWith(na + "/", StringComparison.OrdinalIgnoreCase)
                        || na.StartsWith(nb + "/", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool CoversPath(ModuleCandidate candidate, string relativePath)
    {
        var path = relativePath.Replace('\\', '/');
        foreach (var root in candidate.RootPaths)
        {
            var n = NormalizeRoot(root).TrimEnd('/');
            if (n is "." or "")
            {
                return true;
            }

            if (path.StartsWith(n + "/", StringComparison.OrdinalIgnoreCase)
                || path.Equals(n, StringComparison.OrdinalIgnoreCase)
                || (candidate.ProjectFile is not null
                    && path.Equals(candidate.ProjectFile, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static int TestPenalty(ModuleCandidate c, bool includeTests) =>
        !includeTests && c.Kind.Equals("test", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

    private static int KindRank(string kind) => kind.ToLowerInvariant() switch
    {
        "web" => 0,
        "function" => 1,
        "worker" => 2,
        "console" => 3,
        "library" => 4,
        "configured" => 4,
        "test" => 8,
        "repository" => 9,
        _ => 5
    };

    private static string BuildStableId(ModuleCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.PreferredId)
            && !candidate.PreferredId.Equals("module", StringComparison.OrdinalIgnoreCase))
        {
            // Prefer path-based uniqueness when name is generic
            var pathId = BuildPathBasedId(candidate.RootPaths.FirstOrDefault() ?? candidate.Name, candidate.Name);
            if (candidate.Source is "csproj" or "sln" or "static-analysis")
            {
                // short name first for project modules; fall back to path if needed at EnsureUniqueId
                return Slug(candidate.Name);
            }

            return pathId;
        }

        return BuildPathBasedId(candidate.RootPaths.FirstOrDefault() ?? candidate.Name, candidate.Name);
    }

    private static string BuildPathBasedId(string rootOrPath, string name)
    {
        var path = rootOrPath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(path) || path == ".")
        {
            return string.IsNullOrWhiteSpace(name) ? "module" : Slug(name);
        }

        // Use last 1–2 segments: src/AgentWiki.Core → src-agentwiki-core
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return Slug(parts[^2] + "-" + parts[^1]);
        }

        return Slug(parts[^1]);
    }

    private static string EnsureUniqueId(string baseId, HashSet<string> seen)
    {
        var id = string.IsNullOrWhiteSpace(baseId) ? "module" : baseId;
        if (seen.Add(id))
        {
            return id;
        }

        var i = 2;
        while (!seen.Add($"{id}-{i}"))
        {
            i++;
        }

        return $"{id}-{i}";
    }

    private static string BuildTitle(ModuleCandidate candidate)
    {
        var kind = KindLabel(candidate.Kind);
        if (candidate.Kind is "library" or "unknown" or "configured" or "repository")
        {
            return candidate.Name;
        }

        // "Loans (web)" style only when helpful
        if (candidate.Name.Contains(kind, StringComparison.OrdinalIgnoreCase))
        {
            return candidate.Name;
        }

        return $"{candidate.Name} ({kind})";
    }

    private static string BuildSummary(ModuleCandidate candidate, int relatedCount) =>
        $"{KindLabel(candidate.Kind)} module `{candidate.Name}` from {candidate.Source} "
        + $"at {string.Join(", ", candidate.RootPaths.Select(r => $"`{r}`"))} "
        + $"({relatedCount} related file(s)).";

    private static string KindLabel(string kind) => kind.ToLowerInvariant() switch
    {
        "web" => "Web",
        "function" => "Azure Functions",
        "worker" => "Worker",
        "console" => "Console",
        "test" => "Test",
        "library" => "Library",
        "configured" => "Configured",
        "repository" => "Repository",
        _ => "Project"
    };

    private static string InferKindFromPath(string path)
    {
        var p = path.Replace('\\', '/').ToLowerInvariant();
        if (p.Contains("test", StringComparison.Ordinal) || p.Contains("/tests/", StringComparison.Ordinal))
        {
            return "test";
        }

        if (p.Contains("function", StringComparison.Ordinal))
        {
            return "function";
        }

        if (p.Contains("worker", StringComparison.Ordinal) || p.Contains("host", StringComparison.Ordinal))
        {
            return "worker";
        }

        if (p.Contains("api", StringComparison.Ordinal)
            || p.Contains("web", StringComparison.Ordinal)
            || p.Contains("mvc", StringComparison.Ordinal)
            || p.Contains("blazor", StringComparison.Ordinal))
        {
            return "web";
        }

        if (p.Contains("cli", StringComparison.Ordinal) || p.Contains("console", StringComparison.Ordinal))
        {
            return "console";
        }

        return "library";
    }

    private static string NormalizeRoot(string root)
    {
        var n = root.Replace('\\', '/').Trim();
        if (string.IsNullOrEmpty(n) || n is "." or "./")
        {
            return ".";
        }

        if (!n.EndsWith('/') && !n.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                            && !n.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
                            && !n.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)
                            && !Path.HasExtension(n))
        {
            n += "/";
        }

        return n;
    }

    private static List<TypeSymbolInfo> FilterTypesForModule(
        ModuleDescriptor descriptor,
        StaticAnalysisResult? staticAnalysis)
    {
        if (staticAnalysis is not { PublicTypes.Count: > 0 })
        {
            return [];
        }

        return staticAnalysis.PublicTypes
            .Where(t => BelongsToModule(t.RelativePath, t.ProjectName, descriptor))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<EndpointInfo> FilterEndpointsForModule(
        ModuleDescriptor descriptor,
        StaticAnalysisResult? staticAnalysis)
    {
        if (staticAnalysis is not { Endpoints.Count: > 0 })
        {
            return [];
        }

        return staticAnalysis.Endpoints
            .Where(e => BelongsToModule(e.RelativePath, e.ProjectName, descriptor))
            .ToList();
    }

    private static List<string> FilterEntryPointsForModule(
        ModuleDescriptor descriptor,
        StaticAnalysisResult? staticAnalysis,
        List<string> sourceFiles)
    {
        var fromStatic = staticAnalysis?.EntryPoints
            .Where(p => BelongsToModule(p, projectName: null, descriptor))
            .ToList() ?? [];

        if (fromStatic.Count > 0)
        {
            return fromStatic;
        }

        return sourceFiles
            .Where(p => p.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase)
                        || p.Contains("/Commands/", StringComparison.OrdinalIgnoreCase)
                        || p.EndsWith("Startup.cs", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static List<string> FilterDiForModule(
        ModuleDescriptor descriptor,
        StaticAnalysisResult? staticAnalysis,
        List<TypeSymbolInfo> moduleTypes)
    {
        if (staticAnalysis is not { DiRegistrations.Count: > 0 })
        {
            return [];
        }

        var typeNames = moduleTypes.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matched = staticAnalysis.DiRegistrations
            .Where(d => typeNames.Any(t => d.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (matched.Count > 0)
        {
            return matched;
        }

        var hasEntry = staticAnalysis.EntryPoints.Any(p => BelongsToModule(p, null, descriptor));
        return hasEntry ? staticAnalysis.DiRegistrations.Take(15).ToList() : [];
    }

    private static List<string> FilterObsoleteForModule(
        ModuleDescriptor descriptor,
        StaticAnalysisResult? staticAnalysis,
        List<TypeSymbolInfo> moduleTypes)
    {
        if (staticAnalysis is not { ObsoleteSymbols.Count: > 0 })
        {
            return [];
        }

        var typeNames = moduleTypes.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return staticAnalysis.ObsoleteSymbols
            .Where(s => typeNames.Any(t => s.Contains(t, StringComparison.OrdinalIgnoreCase))
                        || BelongsToModule(s, null, descriptor))
            .ToList();
    }

    private static bool BelongsToModule(string relativePath, string? projectName, ModuleDescriptor descriptor)
    {
        if (!string.IsNullOrWhiteSpace(projectName)
            && (projectName.Equals(descriptor.Name, StringComparison.OrdinalIgnoreCase)
                || projectName.Equals(descriptor.Id, StringComparison.OrdinalIgnoreCase)
                || descriptor.Id.Contains(Slug(projectName), StringComparison.OrdinalIgnoreCase)
                || descriptor.Name.Contains(projectName, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var path = relativePath.Replace('\\', '/');
        foreach (var root in descriptor.RootPaths)
        {
            var prefix = root.Replace('\\', '/').TrimEnd('/') + "/";
            if (prefix is "./" or "/")
            {
                return true;
            }

            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || path.Equals(root.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return descriptor.RelatedFiles.Any(f =>
            f.Equals(relativePath, StringComparison.OrdinalIgnoreCase));
    }

    private static CrossCuttingDocument BuildConcern(
        RepoAnalysisResult analysis,
        string id,
        string title,
        Func<RepoFile, bool> match,
        string summary,
        IReadOnlyList<string> patterns,
        IReadOnlyList<string> guidance)
    {
        var files = analysis.Files
            .Where(match)
            .Select(f => f.RelativePath)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        return new CrossCuttingDocument
        {
            Id = id,
            Title = title,
            Summary = files.Count == 0
                ? summary + " No strongly matching files were found; treat this as baseline guidance."
                : summary,
            Patterns = patterns.ToList(),
            KeyFiles = files,
            Guidance = guidance.ToList(),
            UsedOfflineFallback = true
        };
    }

    private static string Slug(string value)
    {
        var slug = value.Trim().ToLowerInvariant();
        slug = slug.Replace('\\', '/');
        slug = slug.Trim('/');
        // Drop common parenthetical kind suffixes from titles when re-slugging
        var paren = slug.IndexOf(" (", StringComparison.Ordinal);
        if (paren > 0)
        {
            slug = slug[..paren];
        }

        slug = NonSlugChars().Replace(slug, "-");
        slug = CollapseDashes().Replace(slug, "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "module" : slug;
    }

    private sealed class ModuleCandidate
    {
        public string Name { get; set; } = "";
        public string PreferredId { get; set; } = "";
        public List<string> RootPaths { get; set; } = [];
        public string Kind { get; set; } = "library";
        public string Source { get; set; } = "";
        public int Priority { get; set; }
        public string? ProjectFile { get; set; }
        public string Summary { get; set; } = "";
        public List<string> RelatedFiles { get; set; } = [];
    }

    // Project("{type}") = "Name", "path", "{guid}"
    [GeneratedRegex(
        @"Project\(""\{(?<type>[A-Fa-f0-9\-]+)\}""\)\s*=\s*""(?<name>[^""]+)""\s*,\s*""(?<path>[^""]+)""\s*,\s*""\{[^}]+\}""",
        RegexOptions.CultureInvariant)]
    private static partial Regex SlnProjectRegex();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonSlugChars();

    [GeneratedRegex(@"-+")]
    private static partial Regex CollapseDashes();
}
