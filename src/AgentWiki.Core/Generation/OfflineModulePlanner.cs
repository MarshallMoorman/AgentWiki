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

        return new ModuleDocument
        {
            Id = descriptor.Id,
            Title = descriptor.Name,
            Purpose = string.IsNullOrWhiteSpace(descriptor.Summary)
                ? $"Module `{descriptor.Name}` inferred from repository inventory."
                : descriptor.Summary,
            EntryPoints = sourceFiles
                .Where(p => p.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase)
                            || p.Contains("/Commands/", StringComparison.OrdinalIgnoreCase)
                            || p.EndsWith("Startup.cs", StringComparison.OrdinalIgnoreCase))
                .Take(10)
                .ToList(),
            Dependencies = descriptor.RootPaths,
            KeyTypes = sourceFiles
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(15)
                .ToList(),
            HowToExtend =
            [
                $"Add new types under {string.Join(", ", descriptor.RootPaths.Select(p => $"`{p}`"))}.",
                "Keep public surface area documented in this module page when behavior changes.",
                "Prefer existing abstractions/interfaces before introducing new layers."
            ],
            Gotchas =
            [
                "This module page was generated offline from file inventory; verify responsibilities against source.",
                "Related file lists may be capped; inspect the project folder for the full set."
            ],
            RelatedFiles = descriptor.RelatedFiles.Take(MaxFilesPerModule).ToList(),
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
            summary: "Configuration files and project settings discovered in the inventory.",
            patterns:
            [
                "Prefer environment-specific overrides over hard-coded values.",
                "Keep secrets out of committed config; use env vars or secret stores."
            ],
            guidance:
            [
                "Update `.agentwiki/config.json` for AgentWiki settings.",
                "Document new config keys next to the code that consumes them."
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
