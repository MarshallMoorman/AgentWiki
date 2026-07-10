using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Constants;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.App.Services;

/// <summary>
/// Phase 1–2 generator: analyzes the repository and writes a structured placeholder wiki
/// enriched with real inventory stats. LLM-backed content arrives in Phase 3+.
/// </summary>
public sealed class PlaceholderWikiGenerator(
    IRepoAnalyzer repoAnalyzer,
    IOutputWriter outputWriter,
    ILogger<PlaceholderWikiGenerator> logger) : IWikiGenerator
{
    private static readonly JsonSerializerOptions MetaJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <inheritdoc />
    public async Task<GenerationResult> GenerateAsync(
        WikiGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sw = Stopwatch.StartNew();

        try
        {
            if (!Directory.Exists(request.RepoPath))
            {
                return GenerationResult.Fail(
                    $"Repository path does not exist: {request.RepoPath}",
                    sw.Elapsed);
            }

            logger.LogInformation(
                "Starting wiki generation for {RepoPath} (correlationId={CorrelationId}, dryRun={DryRun}, incremental={Incremental})",
                request.RepoPath,
                request.CorrelationId,
                request.DryRun,
                request.Incremental);

            var analysis = await repoAnalyzer
                .AnalyzeAsync(request.RepoPath, request.Config, cancellationToken)
                .ConfigureAwait(false);

            var generatedAt = DateTimeOffset.UtcNow;
            var sections = BuildSections(analysis, request, generatedAt);

            var filesWritten = await outputWriter
                .WriteAsync(request.OutputPath, sections, request.DryRun, cancellationToken)
                .ConfigureAwait(false);

            if (!request.DryRun)
            {
                await WriteMetaAsync(request, analysis, generatedAt, filesWritten, cancellationToken)
                    .ConfigureAwait(false);
            }

            sw.Stop();
            var mode = request.Incremental ? "update" : "generate";
            var warnings = new List<string>
            {
                "Narrative wiki sections are still placeholders (Phase 2). LLM generation arrives in Phase 3.",
            };
            warnings.AddRange(analysis.Warnings);

            return GenerationResult.Ok(
                message:
                $"Phase 2 {mode} complete for '{analysis.RepoName}': analyzed {analysis.Stats.TotalFiles} files " +
                $"({analysis.Stats.SelectedFiles} selected, discovery={analysis.DiscoveryMethod}).",
                outputPath: request.OutputPath,
                filesWritten: filesWritten,
                duration: sw.Elapsed,
                warnings: warnings,
                analysis: analysis);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Wiki generation failed (correlationId={CorrelationId})", request.CorrelationId);
            return GenerationResult.Fail(ex.Message, sw.Elapsed);
        }
    }

    private static IReadOnlyList<WikiSection> BuildSections(
        RepoAnalysisResult analysis,
        WikiGenerationRequest request,
        DateTimeOffset generatedAt)
    {
        var model = request.ModelOverride ?? request.Config.DefaultModel;
        var provider = request.ProviderOverride ?? request.Config.Provider;
        var stats = analysis.Stats;
        var languages = stats.DetectedLanguages.Count == 0
            ? "(none detected)"
            : string.Join(", ", stats.DetectedLanguages);

        var index = new WikiSection(
            Id: "index",
            Title: "Wiki Index",
            RelativePath: "index.md",
            Content: $$"""
                # {{analysis.RepoName}} — AgentWiki

                > **AI-generated documentation** optimized for coding agents.
                > Inventory stats below are real (Phase 2). Narrative sections remain placeholders until Phase 3.

                ## Navigation

                | Page | Description |
                |------|-------------|
                | [Architecture](architecture.md) | High-level system design |
                | [Key Components](key-components.md) | Inventory-backed file map |
                | [Repository Inventory](inventory.md) | Full analysis summary |
                | [Getting Started for Agents](getting-started.md) | How agents should use this wiki |

                ## Quick facts

                - **Repository:** `{{analysis.RepoName}}`
                - **Generated at (UTC):** {{generatedAt:O}}
                - **Mode:** {{(request.Incremental ? "update" : "full generate")}}
                - **Discovery method:** `{{analysis.DiscoveryMethod}}`
                - **Files (after ignores):** {{stats.TotalFiles}}
                - **Selected for analysis:** {{stats.SelectedFiles}}
                - **Approx. lines:** {{stats.TotalLines:N0}}
                - **Languages:** {{languages}}
                - **Model (configured):** `{{model}}`
                - **Provider (configured):** `{{provider}}`
                - **Correlation ID:** `{{request.CorrelationId}}`

                ## How to use this wiki

                1. Read this index and [architecture.md](architecture.md) first.
                2. Use [key-components.md](key-components.md) and [inventory.md](inventory.md) to locate real paths.
                3. Prefer facts from the wiki over guessing — but always verify against source.
                """);

        var architecture = new WikiSection(
            Id: "architecture",
            Title: "Architecture",
            RelativePath: "architecture.md",
            Content: $$"""
                # Architecture Overview

                > Structure inferred from inventory (Phase 2). Semantic architecture narrative arrives in Phase 3.

                ## System context

                ```mermaid
                flowchart LR
                    Agent[Coding Agent] --> Wiki[AgentWiki docs/wiki]
                    Wiki --> Code[Repository source]
                    CLI[agent-wiki CLI] --> Analyzer[RepoAnalyzer]
                    Analyzer --> Code
                    CLI --> Wiki
                    CLI --> LLM[LLM Provider]
                    LLM --> Wiki
                ```

                ## Observed top-level layout

                {{BuildFolderBullets(stats)}}

                ## Category mix

                | Category | Files |
                |----------|------:|
                | SourceCode | {{Cat(stats, FileCategory.SourceCode)}} |
                | Tests | {{Cat(stats, FileCategory.Tests)}} |
                | Configuration | {{Cat(stats, FileCategory.Configuration)}} |
                | Documentation | {{Cat(stats, FileCategory.Documentation)}} |
                | Diagrams | {{Cat(stats, FileCategory.Diagrams)}} |
                | Other | {{Cat(stats, FileCategory.Other)}} |

                ## Implementation status

                | Layer | Responsibility | Status |
                |-------|----------------|--------|
                | CLI | Spectre.Console commands | ✅ Phase 1 |
                | Analysis | Repo inventory + gitignore | ✅ Phase 2 |
                | Generation | Semantic Kernel multi-step pipeline | ⏳ Phase 3–4 |
                | Incremental | Git-based selective updates | ⏳ Phase 5 |

                ## How to extend

                - Phase 3 will replace this page with LLM-authored architecture content using the inventory summary.
                - Customize ignore patterns in `.agentwiki/config.json` to refine what is analyzed.
                """);

        var keyComponents = new WikiSection(
            Id: "key-components",
            Title: "Key Components",
            RelativePath: "key-components.md",
            Content: BuildKeyComponentsMarkdown(analysis));

        var inventory = new WikiSection(
            Id: "inventory",
            Title: "Repository Inventory",
            RelativePath: "inventory.md",
            Content: "# Repository Inventory\n\n" +
                     "> Machine-generated from RepoAnalyzer. Safe to regenerate.\n\n" +
                     "```text\n" +
                     analysis.Summary +
                     "\n```\n");

        var gettingStarted = new WikiSection(
            Id: "getting-started",
            Title: "Getting Started for Agents",
            RelativePath: "getting-started.md",
            Content: $$"""
                # Getting Started for Coding Agents

                This repository maintains an **agent-optimized wiki** at `{{request.Config.OutputPath.Replace('\\', '/')}}/`.

                ## Recommended workflow

                1. Open `index.md` for navigation and quick facts.
                2. Read `architecture.md` before changing structure or dependencies.
                3. Use `key-components.md` and `inventory.md` for real file paths discovered in the repo.
                4. Prefer updating code over inventing undocumented patterns.

                ## Important

                - Inventory data is derived from the live tree (respecting `.gitignore` + config ignores).
                - Narrative content may still be placeholder / AI-generated; verify against source of truth.
                - Do not commit secrets into wiki pages.
                - After meaningful structural changes, re-run `agent-wiki generate` or `update`.
                """);

        return [index, architecture, keyComponents, inventory, gettingStarted];
    }

    private static string BuildKeyComponentsMarkdown(RepoAnalysisResult analysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Key Components");
        sb.AppendLine();
        sb.AppendLine("> File map derived from repository analysis (Phase 2).");
        sb.AppendLine();
        sb.AppendLine("## Languages");
        sb.AppendLine();
        if (analysis.Stats.DetectedLanguages.Count == 0)
        {
            sb.AppendLine("_No languages detected from file extensions._");
        }
        else
        {
            sb.AppendLine("| Language | Files |");
            sb.AppendLine("|----------|------:|");
            foreach (var lang in analysis.Stats.DetectedLanguages)
            {
                analysis.Stats.FilesByLanguage.TryGetValue(lang, out var count);
                sb.AppendLine($"| {lang} | {count} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Selected source files");
        sb.AppendLine();
        var selectedSource = analysis.Files
            .Where(f => f.SelectedForAnalysis && f.Category == FileCategory.SourceCode)
            .Take(40)
            .ToList();

        if (selectedSource.Count == 0)
        {
            sb.AppendLine("_No source files selected._");
        }
        else
        {
            foreach (var file in selectedSource)
            {
                var lines = file.LineCount is int n ? $" (~{n} lines)" : "";
                sb.AppendLine($"- `{file.RelativePath}`{lines}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Selected configuration");
        sb.AppendLine();
        var selectedConfig = analysis.Files
            .Where(f => f.SelectedForAnalysis && f.Category == FileCategory.Configuration)
            .Take(30)
            .ToList();

        if (selectedConfig.Count == 0)
        {
            sb.AppendLine("_No configuration files selected._");
        }
        else
        {
            foreach (var file in selectedConfig)
            {
                sb.AppendLine($"- `{file.RelativePath}`");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Test projects / files (sample)");
        sb.AppendLine();
        foreach (var file in analysis.Files.Where(f => f.Category == FileCategory.Tests).Take(20))
        {
            sb.AppendLine($"- `{file.RelativePath}`");
        }

        if (analysis.Files.Count(f => f.Category == FileCategory.Tests) == 0)
        {
            sb.AppendLine("_No test files detected._");
        }

        return sb.ToString();
    }

    private static string BuildFolderBullets(RepoStats stats)
    {
        if (stats.TopFolders.Count == 0)
        {
            return "_No folders discovered._";
        }

        var sb = new StringBuilder();
        foreach (var folder in stats.TopFolders.Take(12))
        {
            sb.AppendLine($"- `{folder.RelativePath}/` — {folder.FileCount} files");
        }

        return sb.ToString().TrimEnd();
    }

    private static int Cat(RepoStats stats, FileCategory category) =>
        stats.FilesByCategory.TryGetValue(category, out var n) ? n : 0;

    private async Task WriteMetaAsync(
        WikiGenerationRequest request,
        RepoAnalysisResult analysis,
        DateTimeOffset generatedAt,
        IReadOnlyList<string> filesWritten,
        CancellationToken cancellationToken)
    {
        var metaPath = Path.Combine(request.OutputPath, AgentWikiConstants.MetaFileName);
        var meta = new
        {
            tool = AgentWikiConstants.ToolName,
            version = AgentWikiConstants.Version,
            phase = 2,
            mode = request.Incremental ? "update" : "generate",
            generatedAtUtc = generatedAt,
            correlationId = request.CorrelationId,
            repoPath = request.RepoPath,
            model = request.ModelOverride ?? request.Config.DefaultModel,
            provider = request.ProviderOverride ?? request.Config.Provider,
            discoveryMethod = analysis.DiscoveryMethod,
            totalFiles = analysis.Stats.TotalFiles,
            selectedFiles = analysis.Stats.SelectedFiles,
            totalLines = analysis.Stats.TotalLines,
            languages = analysis.Stats.DetectedLanguages,
            filesWritten,
            placeholderNarrative = true
        };

        Directory.CreateDirectory(request.OutputPath);
        await File.WriteAllTextAsync(
                metaPath,
                JsonSerializer.Serialize(meta, MetaJsonOptions),
                cancellationToken)
            .ConfigureAwait(false);

        logger.LogDebug("Wrote metadata to {Path}", metaPath);
    }
}
