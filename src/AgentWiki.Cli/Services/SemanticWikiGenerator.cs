using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Constants;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.Cli.Services;

/// <summary>
/// Phase 3 wiki generator: repository analysis + Semantic Kernel architecture page,
/// with inventory-backed supporting pages.
/// </summary>
public sealed class SemanticWikiGenerator(
    IRepoAnalyzer repoAnalyzer,
    IArchitectureGenerator architectureGenerator,
    IOutputWriter outputWriter,
    ILogger<SemanticWikiGenerator> logger) : IWikiGenerator
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
                "Starting Semantic wiki generation for {RepoPath} (correlationId={CorrelationId}, dryRun={DryRun})",
                request.RepoPath,
                request.CorrelationId,
                request.DryRun);

            var analysis = await repoAnalyzer
                .AnalyzeAsync(request.RepoPath, request.Config, cancellationToken)
                .ConfigureAwait(false);

            var architecture = await architectureGenerator
                .GenerateAsync(
                    analysis,
                    request.Config,
                    request.ModelOverride,
                    request.ProviderOverride,
                    cancellationToken)
                .ConfigureAwait(false);

            var generatedAt = DateTimeOffset.UtcNow;
            var sections = BuildSections(analysis, architecture, request, generatedAt);

            var filesWritten = await outputWriter
                .WriteAsync(request.OutputPath, sections, request.DryRun, cancellationToken)
                .ConfigureAwait(false);

            if (!request.DryRun)
            {
                await WriteMetaAsync(request, analysis, architecture, generatedAt, filesWritten, cancellationToken)
                    .ConfigureAwait(false);
            }

            sw.Stop();
            var mode = request.Incremental ? "update" : "generate";
            var source = architecture.UsedOfflineFallback ? "offline fallback" : "Semantic Kernel";
            var warnings = new List<string>(analysis.Warnings);
            if (architecture.UsedOfflineFallback)
            {
                warnings.Add(
                    "Architecture was generated offline (no LLM credentials or LLM call failed). " +
                    "Configure Azure OpenAI / OpenAI to enable live generation.");
            }

            return GenerationResult.Ok(
                message:
                $"Phase 3 {mode} complete for '{analysis.RepoName}' using {source}: " +
                $"{analysis.Stats.TotalFiles} files analyzed, architecture.md generated.",
                outputPath: request.OutputPath,
                filesWritten: filesWritten,
                duration: sw.Elapsed,
                warnings: warnings,
                analysis: analysis,
                inputTokens: architecture.TokenUsage?.InputTokens ?? 0,
                outputTokens: architecture.TokenUsage?.OutputTokens ?? 0);
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
        ArchitectureDocument architecture,
        WikiGenerationRequest request,
        DateTimeOffset generatedAt)
    {
        var stats = analysis.Stats;
        var languages = stats.DetectedLanguages.Count == 0
            ? "(none detected)"
            : string.Join(", ", stats.DetectedLanguages);
        var archSource = architecture.UsedOfflineFallback ? "offline inventory heuristics" : "Semantic Kernel LLM";

        var index = new WikiSection(
            Id: "index",
            Title: "Wiki Index",
            RelativePath: "index.md",
            Content: $$"""
                # {{analysis.RepoName}} — AgentWiki

                > **Agent-optimized documentation.** Architecture is generated via {{archSource}}.

                ## Navigation

                | Page | Description |
                |------|-------------|
                | [Architecture](architecture.md) | Structured system design (Phase 3) |
                | [Key Components](key-components.md) | Inventory-backed file map |
                | [Repository Inventory](inventory.md) | Full analysis summary |
                | [Getting Started for Agents](getting-started.md) | How agents should use this wiki |

                ## Quick facts

                - **Repository:** `{{analysis.RepoName}}`
                - **Generated at (UTC):** {{generatedAt:O}}
                - **Mode:** {{(request.Incremental ? "update" : "full generate")}}
                - **Architecture source:** {{archSource}}
                - **Discovery method:** `{{analysis.DiscoveryMethod}}`
                - **Files (after ignores):** {{stats.TotalFiles}}
                - **Selected for analysis:** {{stats.SelectedFiles}}
                - **Approx. lines:** {{stats.TotalLines:N0}}
                - **Languages:** {{languages}}
                - **Correlation ID:** `{{request.CorrelationId}}`

                ## How to use this wiki

                1. Start with [architecture.md](architecture.md).
                2. Use [key-components.md](key-components.md) / [inventory.md](inventory.md) for real paths.
                3. Verify AI-generated guidance against source before large changes.
                """);

        var architectureSection = new WikiSection(
            Id: "architecture",
            Title: architecture.Title,
            RelativePath: "architecture.md",
            Content: ArchitectureMarkdownRenderer.Render(architecture, analysis.RepoName));

        var keyComponents = new WikiSection(
            Id: "key-components",
            Title: "Key Components",
            RelativePath: "key-components.md",
            Content: BuildKeyComponentsMarkdown(analysis, architecture));

        var inventory = new WikiSection(
            Id: "inventory",
            Title: "Repository Inventory",
            RelativePath: "inventory.md",
            Content: "# Repository Inventory\n\n" +
                     "> Machine-generated from RepoAnalyzer.\n\n" +
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

                1. Read `architecture.md` for system structure and gotchas.
                2. Use `key-components.md` and `inventory.md` for concrete file paths.
                3. Prefer existing patterns over inventing new layers.

                ## Important

                - Architecture content may be AI-generated; verify against source.
                - Inventory is derived from the live tree (`.gitignore` + config ignores).
                - Re-run `agent-wiki generate` or `update` after structural changes.
                """);

        return [index, architectureSection, keyComponents, inventory, gettingStarted];
    }

    private static string BuildKeyComponentsMarkdown(
        RepoAnalysisResult analysis,
        ArchitectureDocument architecture)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Key Components");
        sb.AppendLine();
        sb.AppendLine("> Combines structured architecture components with live inventory.");
        sb.AppendLine();

        if (architecture.KeyComponents.Count > 0)
        {
            sb.AppendLine("## From architecture generation");
            sb.AppendLine();
            foreach (var component in architecture.KeyComponents)
            {
                var path = string.IsNullOrWhiteSpace(component.Path) ? "" : $" (`{component.Path}`)";
                sb.AppendLine($"- **{component.Name}**{path}: {component.Purpose}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("## Languages (inventory)");
        sb.AppendLine();
        if (analysis.Stats.DetectedLanguages.Count == 0)
        {
            sb.AppendLine("_No languages detected._");
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
        foreach (var file in analysis.Files.Where(f => f.SelectedForAnalysis && f.Category == FileCategory.SourceCode).Take(40))
        {
            var lines = file.LineCount is int n ? $" (~{n} lines)" : "";
            sb.AppendLine($"- `{file.RelativePath}`{lines}");
        }

        return sb.ToString();
    }

    private async Task WriteMetaAsync(
        WikiGenerationRequest request,
        RepoAnalysisResult analysis,
        ArchitectureDocument architecture,
        DateTimeOffset generatedAt,
        IReadOnlyList<string> filesWritten,
        CancellationToken cancellationToken)
    {
        var metaPath = Path.Combine(request.OutputPath, AgentWikiConstants.MetaFileName);
        var meta = new
        {
            tool = AgentWikiConstants.ToolName,
            version = AgentWikiConstants.Version,
            phase = 3,
            mode = request.Incremental ? "update" : "generate",
            generatedAtUtc = generatedAt,
            correlationId = request.CorrelationId,
            repoPath = request.RepoPath,
            model = request.ModelOverride ?? request.Config.DefaultModel,
            provider = request.ProviderOverride ?? request.Config.Provider,
            discoveryMethod = analysis.DiscoveryMethod,
            architectureSource = architecture.UsedOfflineFallback ? "offline" : "semantic-kernel",
            totalFiles = analysis.Stats.TotalFiles,
            selectedFiles = analysis.Stats.SelectedFiles,
            totalLines = analysis.Stats.TotalLines,
            inputTokens = architecture.TokenUsage?.InputTokens ?? 0,
            outputTokens = architecture.TokenUsage?.OutputTokens ?? 0,
            languages = analysis.Stats.DetectedLanguages,
            filesWritten
        };

        Directory.CreateDirectory(request.OutputPath);
        await File.WriteAllTextAsync(
                metaPath,
                JsonSerializer.Serialize(meta, MetaJsonOptions),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
