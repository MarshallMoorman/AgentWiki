using System.Text.RegularExpressions;
using AgentWiki.Core.Models;

namespace AgentWiki.Core.Generation;

/// <summary>
/// Heuristic module identification from inventory (no LLM).
/// </summary>
public static partial class OfflineModulePlanner
{
    private const int MaxModules = 8;
    private const int MaxFilesPerModule = 25;

    public static ModulePlan Plan(RepoAnalysisResult analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        var modules = new List<ModuleDescriptor>();

        // Prefer project files as module roots.
        var projects = analysis.Files
            .Where(f => f.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                        || f.RelativePath.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
                        || f.RelativePath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var project in projects.Take(MaxModules))
        {
            var dir = Path.GetDirectoryName(project.RelativePath)?.Replace('\\', '/') ?? "";
            var name = Path.GetFileNameWithoutExtension(project.RelativePath);
            var id = Slug(name);
            var related = analysis.Files
                .Where(f => dir.Length == 0
                    ? !f.RelativePath.Contains('/')
                    : f.RelativePath.StartsWith(dir + "/", StringComparison.OrdinalIgnoreCase)
                      || f.RelativePath.Equals(project.RelativePath, StringComparison.OrdinalIgnoreCase))
                .Select(f => f.RelativePath)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Take(MaxFilesPerModule)
                .ToList();

            modules.Add(new ModuleDescriptor
            {
                Id = id,
                Name = name,
                Summary = $"Project module defined by `{project.RelativePath}`.",
                RootPaths = string.IsNullOrEmpty(dir) ? ["."] : [dir + "/"],
                RelatedFiles = related
            });
        }

        // Fall back to top-level folders when few/no projects.
        if (modules.Count == 0)
        {
            foreach (var folder in analysis.Stats.TopFolders
                         .Where(f => f.RelativePath is not "(root)")
                         .OrderByDescending(f => f.FileCount)
                         .Take(MaxModules))
            {
                var related = analysis.Files
                    .Where(f => f.RelativePath.StartsWith(folder.RelativePath + "/", StringComparison.OrdinalIgnoreCase))
                    .Select(f => f.RelativePath)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .Take(MaxFilesPerModule)
                    .ToList();

                modules.Add(new ModuleDescriptor
                {
                    Id = Slug(folder.RelativePath),
                    Name = folder.RelativePath,
                    Summary = $"Top-level area `{folder.RelativePath}/` ({folder.FileCount} files).",
                    RootPaths = [folder.RelativePath + "/"],
                    RelatedFiles = related
                });
            }
        }

        if (modules.Count == 0)
        {
            modules.Add(new ModuleDescriptor
            {
                Id = "repository",
                Name = analysis.RepoName,
                Summary = "Entire repository treated as a single module.",
                RootPaths = ["."],
                RelatedFiles = analysis.Files
                    .Where(f => f.SelectedForAnalysis)
                    .Select(f => f.RelativePath)
                    .Take(MaxFilesPerModule)
                    .ToList()
            });
        }

        // Ensure unique ids.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
        {
            var baseId = module.Id;
            var i = 2;
            while (!seen.Add(module.Id))
            {
                module.Id = $"{baseId}-{i++}";
            }
        }

        return new ModulePlan
        {
            Modules = modules,
            UsedOfflineFallback = true
        };
    }

    public static ModuleDocument BuildModuleDocument(ModuleDescriptor descriptor, RepoAnalysisResult analysis)
    {
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
            RelatedFiles = descriptor.RelatedFiles.Take(MaxFilesPerModule).ToList(),
            Endpoints = moduleEndpoints.ToList(),
            UsedOfflineFallback = true
        };
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

        // Prefer DI lines that mention types in this module; otherwise include all if module has Program.cs.
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
                || descriptor.Id.Contains(Slug(projectName), StringComparison.OrdinalIgnoreCase)))
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
        slug = NonSlugChars().Replace(slug, "-");
        slug = CollapseDashes().Replace(slug, "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "module" : slug;
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonSlugChars();

    [GeneratedRegex(@"-+")]
    private static partial Regex CollapseDashes();
}
