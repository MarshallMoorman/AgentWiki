using AgentWiki.Core.Analysis;
using AgentWiki.Core.Models;

namespace AgentWiki.Core.Generation;

/// <summary>
/// Deterministic architecture document built only from inventory heuristics
/// (used when LLM credentials are missing or as a testable fallback).
/// </summary>
public static class OfflineArchitectureGenerator
{
    public static ArchitectureDocument Generate(RepoAnalysisResult analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        var stats = analysis.Stats;
        var languages = stats.DetectedLanguages.Count == 0
            ? "unknown"
            : string.Join(", ", stats.DetectedLanguages);

        var layers = stats.TopFolders
            .Where(f => f.RelativePath is not "(root)")
            .Take(8)
            .Select(f => new ArchitectureLayer
            {
                Name = f.RelativePath,
                Responsibility = InferFolderResponsibility(f.RelativePath),
                KeyPaths = [$"{f.RelativePath}/"]
            })
            .ToList();

        if (layers.Count == 0)
        {
            layers.Add(new ArchitectureLayer
            {
                Name = "Repository root",
                Responsibility = "Primary codebase content",
                KeyPaths = ["."]
            });
        }

        var staticAnalysis = analysis.StaticAnalysis;
        var keyComponents = BuildKeyComponents(analysis, staticAnalysis);

        var policyFiles = analysis.Files
            .Where(f => IsPolicyPath(f.RelativePath))
            .Select(f => f.RelativePath)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var pipelineFiles = analysis.Files
            .Where(f => IsPipelinePath(f.RelativePath))
            .Select(f => f.RelativePath)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var dataFlows = new List<string>
        {
            "Developer/agent runs CLI or build tooling against repository source.",
            "Configuration (csproj/json/yml) drives project composition and runtime settings.",
            "Tests exercise source modules under tests/ or *.Tests projects."
        };

        if (pipelineFiles.Count > 0)
        {
            dataFlows.Add(
                "CI/CD pipelines build and deploy the service: "
                + string.Join(", ", pipelineFiles.Take(5).Select(p => $"`{p}`"))
                + (pipelineFiles.Count > 5 ? ", …" : "")
                + ".");
        }

        if (policyFiles.Count > 0)
        {
            dataFlows.Add(
                "API Management (or edge) policies under Policies/ are deployed with the app and shape inbound/outbound request behavior: "
                + string.Join(", ", policyFiles.Take(5).Select(p => $"`{p}`"))
                + (policyFiles.Count > 5 ? ", …" : "")
                + ".");
        }

        var decisions = new List<string>
        {
            "Prefer inventory-backed paths over invented module names.",
            "Treat generated wiki output under docs/wiki as derived artifacts."
        };
        if (policyFiles.Count > 0 || pipelineFiles.Count > 0)
        {
            decisions.Add(
                "Include Policies/ and pipeline YAML when reasoning about deployment, routing, and runtime auth/throttle behavior.");
        }

        if (staticAnalysis is { Succeeded: true, UsedRoslyn: true })
        {
            if (staticAnalysis.EntryPoints.Count > 0)
            {
                dataFlows.Insert(0,
                    "Application entry points: "
                    + string.Join(", ", staticAnalysis.EntryPoints.Take(6).Select(p => $"`{p}`"))
                    + ".");
            }

            if (staticAnalysis.Endpoints.Count > 0)
            {
                dataFlows.Add(
                    $"Discovered {staticAnalysis.Endpoints.Count} HTTP/Function endpoint(s) via static analysis "
                    + $"(e.g. {string.Join(", ", staticAnalysis.Endpoints.Take(3).Select(e => $"`{e.HttpMethod} {e.Route}`"))}"
                    + (staticAnalysis.Endpoints.Count > 3 ? ", …" : "")
                    + ").");
            }

            if (staticAnalysis.DiRegistrations.Count > 0)
            {
                decisions.Add(
                    "DI registration hints observed in source: "
                    + string.Join(", ", staticAnalysis.DiRegistrations.Take(8).Select(d => $"`{d}`"))
                    + (staticAnalysis.DiRegistrations.Count > 8 ? ", …" : "")
                    + ".");
            }
        }

        var mermaid = BuildMermaid(analysis, staticAnalysis);
        var gotchas = new List<string>
        {
            "Offline mode cannot infer runtime topology or domain rules—verify against source.",
            "Ignored paths (bin/obj/node_modules/docs/wiki) are intentionally excluded from analysis."
        };
        if (staticAnalysis is { ObsoleteSymbols.Count: > 0 })
        {
            gotchas.Add(
                "Obsolete markers found in source: "
                + string.Join(", ", staticAnalysis.ObsoleteSymbols.Take(6).Select(s => $"`{s}`"))
                + (staticAnalysis.ObsoleteSymbols.Count > 6 ? ", …" : "")
                + ".");
        }

        var howToExtend = new List<string>
        {
            "Add source under existing top-level folders to match observed layout.",
            "Configure Azure OpenAI / OpenAI credentials to upgrade this page to LLM-authored architecture.",
            "Adjust IgnorePatterns and MaxFilesToAnalyze in .agentwiki/config.json to refine inventory."
        };
        if (staticAnalysis is { PublicTypes.Count: > 0 })
        {
            howToExtend.Insert(0,
                "Prefer extending existing public types/interfaces discovered by static analysis before inventing new layers.");
        }

        var summary =
            $"{analysis.RepoName} is a {languages} codebase with {stats.TotalFiles} tracked files " +
            $"(~{stats.TotalLines:N0} lines of text). Inventory discovery used `{analysis.DiscoveryMethod}`. " +
            "This document was produced offline from repository analysis (no LLM call).";
        if (staticAnalysis is { Succeeded: true, FilesAnalyzed: > 0 })
        {
            summary += " " + staticAnalysis.Summary;
        }

        var systemContext =
            $"Primary languages: {languages}. " +
            $"Category mix — Source: {Cat(stats, FileCategory.SourceCode)}, " +
            $"Tests: {Cat(stats, FileCategory.Tests)}, " +
            $"Config: {Cat(stats, FileCategory.Configuration)}, " +
            $"Docs: {Cat(stats, FileCategory.Documentation)}."
            + (policyFiles.Count > 0 ? $" API Management / edge policies: {policyFiles.Count} file(s)." : "")
            + (pipelineFiles.Count > 0 ? $" CI/CD pipeline definition(s): {pipelineFiles.Count}." : "");
        if (staticAnalysis is { Projects.Count: > 0 })
        {
            systemContext += " Projects: "
                             + string.Join(", ", staticAnalysis.Projects.Take(8).Select(p => $"`{p.Name}` ({p.Kind})"))
                             + (staticAnalysis.Projects.Count > 8 ? ", …" : "")
                             + ".";
        }

        return new ArchitectureDocument
        {
            Title = $"{analysis.RepoName} Architecture Overview",
            Summary = summary,
            SystemContext = systemContext,
            Layers = layers,
            KeyComponents = keyComponents,
            DataFlows = dataFlows,
            Decisions = decisions,
            Gotchas = gotchas,
            HowToExtend = howToExtend,
            MermaidDiagram = mermaid,
            UsedOfflineFallback = true
        };
    }

    private static List<ArchitectureComponent> BuildKeyComponents(
        RepoAnalysisResult analysis,
        StaticAnalysisResult? staticAnalysis)
    {
        var components = new List<ArchitectureComponent>();

        if (staticAnalysis is { Succeeded: true })
        {
            foreach (var project in staticAnalysis.Projects.Take(10))
            {
                components.Add(new ArchitectureComponent
                {
                    Name = project.Name,
                    Path = project.RelativePath,
                    Purpose =
                        $"{project.Kind} project — {project.PublicTypeCount} public type(s)"
                        + (project.EndpointCount > 0 ? $", {project.EndpointCount} endpoint(s)" : "")
                        + (project.EntryPoints.Count > 0
                            ? $"; entry: {string.Join(", ", project.EntryPoints.Take(3).Select(e => $"`{e}`"))}"
                            : "")
                });
            }

            foreach (var type in staticAnalysis.PublicTypes
                         .Where(t => t.Kind is "class" or "interface" or "record")
                         .OrderBy(t => t.Name.EndsWith("Controller", StringComparison.Ordinal) ? 0
                             : t.Kind == "interface" ? 1 : 2)
                         .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                         .Take(12))
            {
                var attrs = type.Attributes.Count > 0
                    ? " [" + string.Join(", ", type.Attributes.Take(4)) + "]"
                    : "";
                components.Add(new ArchitectureComponent
                {
                    Name = type.Name,
                    Path = type.RelativePath,
                    Purpose = $"Public {type.Kind}"
                              + (string.IsNullOrWhiteSpace(type.Namespace) ? "" : $" in `{type.Namespace}`")
                              + attrs
                });
            }
        }

        if (components.Count >= 8)
        {
            return components.Take(20).ToList();
        }

        // Inventory fallback / supplement
        foreach (var f in analysis.Files
                     .Where(f => f.SelectedForAnalysis && f.Category is FileCategory.SourceCode or FileCategory.Configuration)
                     .OrderBy(f => FileCategorizer.IsInfrastructurePath(f.RelativePath) ? 0
                         : f.Category == FileCategory.SourceCode ? 1 : 2)
                     .ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
                     .Take(15))
        {
            if (components.Any(c => string.Equals(c.Path, f.RelativePath, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(c.Name, Path.GetFileNameWithoutExtension(f.RelativePath), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            components.Add(new ArchitectureComponent
            {
                Name = Path.GetFileName(f.RelativePath),
                Path = f.RelativePath,
                Purpose = InferComponentPurpose(f)
            });
        }

        return components.Take(20).ToList();
    }

    private static string InferComponentPurpose(RepoFile f)
    {
        if (IsPolicyPath(f.RelativePath))
        {
            return "API Management / edge policy deployed with the service";
        }

        if (IsPipelinePath(f.RelativePath))
        {
            return "CI/CD build and deployment pipeline";
        }

        return f.Category == FileCategory.Configuration
            ? "Configuration / project definition"
            : $"Source file ({f.Language ?? f.Extension ?? "code"})";
    }

    private static bool IsPolicyPath(string relativePath)
    {
        var p = relativePath.Replace('\\', '/');
        return p.Contains("/policies/", StringComparison.OrdinalIgnoreCase)
               || p.StartsWith("policies/", StringComparison.OrdinalIgnoreCase)
               || p.EndsWith("policy.xml", StringComparison.OrdinalIgnoreCase)
               || p.Contains("-policy.xml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPipelinePath(string relativePath)
    {
        var p = relativePath.Replace('\\', '/').ToLowerInvariant();
        var name = Path.GetFileName(p);
        return name.Contains("pipeline", StringComparison.Ordinal)
               || p.Contains("/pipelines/", StringComparison.Ordinal)
               || name.StartsWith("azure-pipelines", StringComparison.Ordinal)
               || name.StartsWith("azure-build-pipeline", StringComparison.Ordinal);
    }

    private static int Cat(RepoStats stats, FileCategory category) =>
        stats.FilesByCategory.TryGetValue(category, out var n) ? n : 0;

    private static string InferFolderResponsibility(string folder) =>
        folder.ToLowerInvariant() switch
        {
            "src" => "Primary application and library source",
            "tests" or "test" => "Automated tests",
            "docs" => "Human/agent documentation",
            "scripts" => "Automation and tooling scripts",
            "build" => "Build pipelines and assets",
            "policies" or "policy" => "API Management / edge policies deployed with the service",
            "pipelines" or "pipeline" => "CI/CD pipeline definitions",
            "infra" or "infrastructure" => "Infrastructure as code and deployment assets",
            "samples" or "examples" => "Sample code",
            ".github" => "GitHub workflows and community files",
            _ => $"Project area `{folder}`"
        };

    private static string BuildMermaid(RepoAnalysisResult analysis, StaticAnalysisResult? staticAnalysis)
    {
        if (staticAnalysis is { Projects.Count: > 0 }
            && staticAnalysis.Projects.Any(p => p.RelativePath is not "." and not "(repository)"))
        {
            var lines = new List<string> { "flowchart TB", $"    Root[{SanitizeMermaid(analysis.RepoName)}]" };
            var i = 0;
            foreach (var project in staticAnalysis.Projects.Take(8))
            {
                var id = $"P{i++}";
                lines.Add($"    Root --> {id}[{SanitizeMermaid(project.Name)} ({project.Kind})]");
            }

            return string.Join('\n', lines);
        }

        var nodes = analysis.Stats.TopFolders
            .Where(f => f.RelativePath is not "(root)")
            .Take(6)
            .Select((f, i) => (Id: $"N{i}", Label: f.RelativePath))
            .ToList();

        if (nodes.Count == 0)
        {
            return """
                flowchart TB
                    Root[Repository root] --> Code[Source]
                """;
        }

        var folderLines = new List<string> { "flowchart TB", $"    Root[{SanitizeMermaid(analysis.RepoName)}]" };
        foreach (var node in nodes)
        {
            folderLines.Add($"    Root --> {node.Id}[{SanitizeMermaid(node.Label)}]");
        }

        return string.Join('\n', folderLines);
    }

    private static string SanitizeMermaid(string label) =>
        label.Replace('[', '(').Replace(']', ')').Replace('"', '\'');
}
