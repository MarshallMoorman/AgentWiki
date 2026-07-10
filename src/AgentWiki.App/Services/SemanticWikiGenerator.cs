using System.Diagnostics;
using System.Text.Json;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Constants;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.App.Services;

/// <summary>
/// Phase 5 wiki generator: multi-step orchestration, AGENTS.md bootstrap,
/// and git-based incremental updates with last-run tracking.
/// </summary>
public sealed class SemanticWikiGenerator(
    IRepoAnalyzer repoAnalyzer,
    IWikiGenerationOrchestrator orchestrator,
    IOutputWriter outputWriter,
    IAgentBootstrapper agentBootstrapper,
    IChangeDetector changeDetector,
    ILastRunStore lastRunStore,
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
                "Starting wiki generation for {RepoPath} (correlationId={CorrelationId}, incremental={Incremental}, dryRun={DryRun})",
                request.RepoPath,
                request.CorrelationId,
                request.Incremental,
                request.DryRun);

            request.Progress?.Report("Analyzing repository inventory…");
            var analysis = await repoAnalyzer
                .AnalyzeAsync(request.RepoPath, request.Config, cancellationToken)
                .ConfigureAwait(false);

            ChangeDetectionResult? changes = request.ChangeDetection;
            IncrementalScope scope = IncrementalScope.Full();

            if (request.Incremental)
            {
                request.Progress?.Report("Detecting git changes since last run…");
                changes ??= await changeDetector
                    .DetectAsync(request.RepoPath, request.Config, analysis, cancellationToken)
                    .ConfigureAwait(false);

                if (changes.NoChanges)
                {
                    sw.Stop();
                    return GenerationResult.Ok(
                        message:
                        $"Phase 5 update: no relevant changes since last run " +
                        $"({changes.BaselineCommitSha?[..Math.Min(7, changes.BaselineCommitSha.Length)] ?? "unknown"}). Wiki left unchanged.",
                        outputPath: request.OutputPath,
                        filesWritten: [],
                        duration: sw.Elapsed,
                        warnings: changes.Warnings.ToList(),
                        analysis: analysis,
                        changeDetection: changes);
                }

                scope = IncrementalScope.FromChanges(changes);
                logger.LogInformation(
                    "Incremental scope: full={Full}, architecture={Arch}, modules={Modules}, crossCutting={Cross}, reason={Reason}",
                    scope.IsFull,
                    scope.Architecture,
                    scope.AllModules ? "*" : string.Join(',', scope.ModuleIds),
                    scope.AllCrossCutting ? "*" : string.Join(',', scope.CrossCuttingIds),
                    changes.Reason);
            }

            request = new WikiGenerationRequest
            {
                Config = request.Config,
                RepoPath = request.RepoPath,
                OutputPath = request.OutputPath,
                Force = request.Force,
                DryRun = request.DryRun,
                Incremental = request.Incremental,
                ChangeDetection = changes,
                Scope = scope,
                ModelOverride = request.ModelOverride,
                ProviderOverride = request.ProviderOverride,
                CorrelationId = request.CorrelationId,
                Progress = request.Progress
            };

            var bundle = await orchestrator
                .GenerateAsync(analysis, request, scope, cancellationToken)
                .ConfigureAwait(false);

            request.Progress?.Report(
                request.DryRun
                    ? "Dry run — computing wiki pages without writing…"
                    : "Writing wiki Markdown files…");
            var sectionsToWrite = FilterSectionsForWrite(bundle.Sections, scope, request.OutputPath, request.Incremental);
            var filesWritten = await outputWriter
                .WriteAsync(request.OutputPath, sectionsToWrite, request.DryRun, cancellationToken)
                .ConfigureAwait(false);

            var warnings = new List<string>(bundle.Warnings);
            if (changes is not null)
            {
                warnings.AddRange(changes.Warnings);
                warnings.Add($"Change detection: {changes.Reason}");
                if (!changes.RequiresFullRegeneration && !changes.NoChanges)
                {
                    warnings.Add(
                        $"Selective update — architecture={scope.Architecture}, " +
                        $"modules=[{string.Join(", ", scope.ModuleIds)}], " +
                        $"cross-cutting=[{string.Join(", ", scope.CrossCuttingIds)}].");
                }
            }

            if (!request.DryRun)
            {
                request.Progress?.Report("Updating meta and AGENTS.md bootstrap…");
                await WriteMetaAsync(request, analysis, bundle, filesWritten, changes, cancellationToken)
                    .ConfigureAwait(false);

                var bootstrap = await agentBootstrapper
                    .EnsureInstructionsAsync(
                        request.RepoPath,
                        request.Config.AgentMdPath,
                        request.Config.OutputPath,
                        dryRun: false,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!bootstrap.Success)
                {
                    warnings.Add($"AGENTS.md bootstrap failed: {bootstrap.Error}");
                }
                else if (bootstrap.Action is BootstrapAction.Created or BootstrapAction.Updated)
                {
                    warnings.Add($"Agent bootstrap: {bootstrap.Message}");
                }

                await SaveLastRunAsync(request, analysis, bundle, filesWritten, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                var bootstrap = await agentBootstrapper
                    .EnsureInstructionsAsync(
                        request.RepoPath,
                        request.Config.AgentMdPath,
                        request.Config.OutputPath,
                        dryRun: true,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (bootstrap.Success)
                {
                    warnings.Add(bootstrap.Message);
                }
            }

            sw.Stop();
            var mode = request.Incremental ? "update" : "generate";
            var source = bundle.UsedOfflineFallback ? "offline/multi-step" : "Semantic Kernel multi-step";
            var scopeLabel = scope.IsFull ? "full" : "selective";
            var modelName = request.ModelOverride ?? request.Config.DefaultModel;
            var cost = CostEstimator.Estimate(
                modelName,
                bundle.TokenUsage.InputTokens,
                bundle.TokenUsage.OutputTokens);

            return GenerationResult.Ok(
                message:
                $"{mode} ({scopeLabel}) complete for '{analysis.RepoName}' using {source}: " +
                $"{analysis.Stats.TotalFiles} files analyzed, {bundle.Modules.Count} modules, " +
                $"{filesWritten.Count} wiki files written.",
                outputPath: request.OutputPath,
                filesWritten: filesWritten,
                duration: sw.Elapsed,
                warnings: warnings,
                analysis: analysis,
                inputTokens: bundle.TokenUsage.InputTokens,
                outputTokens: bundle.TokenUsage.OutputTokens,
                changeDetection: changes,
                costEstimate: cost);
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

    private static IReadOnlyList<WikiSection> FilterSectionsForWrite(
        IReadOnlyList<WikiSection> sections,
        IncrementalScope scope,
        string outputPath,
        bool incremental)
    {
        if (!incremental || scope.IsFull)
        {
            return sections;
        }

        var support = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "index.md",
            "key-components.md",
            "data-flows.md",
            "inventory.md",
            "glossary.md",
            "getting-started.md"
        };

        return sections.Where(section =>
        {
            var rel = section.RelativePath.Replace('\\', '/');
            if (support.Contains(rel))
            {
                return true;
            }

            if (rel.Equals("architecture.md", StringComparison.OrdinalIgnoreCase))
            {
                return scope.Architecture || !File.Exists(Path.Combine(outputPath, rel));
            }

            if (rel.StartsWith("modules/", StringComparison.OrdinalIgnoreCase))
            {
                var id = Path.GetFileNameWithoutExtension(rel);
                return scope.AllModules
                       || scope.ModuleIds.Contains(id)
                       || !File.Exists(Path.Combine(outputPath, rel.Replace('/', Path.DirectorySeparatorChar)));
            }

            if (rel.StartsWith("cross-cutting/", StringComparison.OrdinalIgnoreCase))
            {
                var id = Path.GetFileNameWithoutExtension(rel);
                return scope.AllCrossCutting
                       || scope.CrossCuttingIds.Contains(id)
                       || !File.Exists(Path.Combine(outputPath, rel.Replace('/', Path.DirectorySeparatorChar)));
            }

            return true;
        }).ToList();
    }

    private async Task SaveLastRunAsync(
        WikiGenerationRequest request,
        RepoAnalysisResult analysis,
        WikiBundle bundle,
        IReadOnlyList<string> filesWritten,
        CancellationToken cancellationToken)
    {
        var sha = request.ChangeDetection?.CurrentCommitSha
                  ?? await GitProcess.TryGetHeadShaAsync(request.RepoPath, cancellationToken).ConfigureAwait(false);

        var state = new LastRunState
        {
            CommitSha = sha,
            TimestampUtc = DateTimeOffset.UtcNow,
            CorrelationId = request.CorrelationId,
            Mode = request.Incremental ? "update" : "generate",
            OutputPath = request.Config.OutputPath,
            FilesWritten = filesWritten.ToList(),
            ModuleIds = bundle.Modules.Select(m => m.Id).ToList(),
            TotalFiles = analysis.Stats.TotalFiles,
            ToolVersion = AgentWikiConstants.Version
        };

        await lastRunStore.SaveAsync(request.RepoPath, state, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteMetaAsync(
        WikiGenerationRequest request,
        RepoAnalysisResult analysis,
        WikiBundle bundle,
        IReadOnlyList<string> filesWritten,
        ChangeDetectionResult? changes,
        CancellationToken cancellationToken)
    {
        var metaPath = Path.Combine(request.OutputPath, AgentWikiConstants.MetaFileName);
        var meta = new
        {
            tool = AgentWikiConstants.ToolName,
            version = AgentWikiConstants.Version,
            phase = 6,
            mode = request.Incremental ? "update" : "generate",
            generatedAtUtc = DateTimeOffset.UtcNow,
            correlationId = request.CorrelationId,
            repoPath = request.RepoPath,
            model = request.ModelOverride ?? request.Config.DefaultModel,
            provider = request.ProviderOverride ?? request.Config.Provider,
            discoveryMethod = analysis.DiscoveryMethod,
            architectureSource = bundle.Architecture.UsedOfflineFallback ? "offline" : "semantic-kernel",
            steps = bundle.StepsCompleted,
            modules = bundle.Modules.Select(m => m.Id).ToList(),
            crossCutting = bundle.CrossCutting.Select(c => c.Id).ToList(),
            totalFiles = analysis.Stats.TotalFiles,
            selectedFiles = analysis.Stats.SelectedFiles,
            totalLines = analysis.Stats.TotalLines,
            inputTokens = bundle.TokenUsage.InputTokens,
            outputTokens = bundle.TokenUsage.OutputTokens,
            usedOfflineFallback = bundle.UsedOfflineFallback,
            languages = analysis.Stats.DetectedLanguages,
            changeDetection = changes is null
                ? null
                : new
                {
                    changes.HasBaseline,
                    changes.RequiresFullRegeneration,
                    changes.NoChanges,
                    changes.BaselineCommitSha,
                    changes.CurrentCommitSha,
                    changedFileCount = changes.ChangedFiles.Count,
                    changes.ArchitectureAffected,
                    affectedModules = changes.AffectedModuleIds.ToList(),
                    affectedCrossCutting = changes.AffectedCrossCuttingIds.ToList(),
                    changes.Reason,
                    changes.DetectionMethod
                },
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
