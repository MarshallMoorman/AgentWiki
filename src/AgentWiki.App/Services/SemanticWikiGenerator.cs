using System.Diagnostics;
using System.Text.Json;
using AgentWiki.App.Infrastructure;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core;
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
    IStaticAnalyzer staticAnalyzer,
    IWikiGenerationOrchestrator orchestrator,
    IOutputWriter outputWriter,
    IAgentBootstrapper agentBootstrapper,
    IAgentsMdGenerator agentsMdGenerator,
    IReadmeGenerator readmeGenerator,
    IChangeDetector changeDetector,
    ILastRunStore lastRunStore,
    IRunTelemetry runTelemetry,
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
                    sw.Elapsed,
                    request.CorrelationId);
            }

            logger.LogInformation(
                "Starting wiki generation for {RepoPath} (correlationId={CorrelationId}, incremental={Incremental}, dryRun={DryRun})",
                request.RepoPath,
                request.CorrelationId,
                request.Incremental,
                request.DryRun);

            if (runTelemetry is ApplicationInsightsRunTelemetry ai)
            {
                ai.Configure(request.Config);
            }

            request.Progress?.Report("Analyzing repository inventory…");
            var analysis = await repoAnalyzer
                .AnalyzeAsync(request.RepoPath, request.Config, cancellationToken)
                .ConfigureAwait(false);

            // Optional Roslyn enrichment (offline-safe; skipped for non-.NET / on failure).
            if (request.Config.EnableRoslynAnalysis)
            {
                request.Progress?.Report("Running static analysis (Roslyn)…");
                var staticResult = await staticAnalyzer
                    .AnalyzeAsync(analysis, request.Config, cancellationToken)
                    .ConfigureAwait(false);
                analysis.StaticAnalysis = staticResult;
                if (staticResult.Warnings.Count > 0)
                {
                    logger.LogDebug(
                        "Static analysis warnings: {Warnings}",
                        string.Join("; ", staticResult.Warnings.Take(5)));
                }
            }

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
            var writeResult = await outputWriter
                .WriteAsync(request.OutputPath, sectionsToWrite, request.DryRun, cancellationToken)
                .ConfigureAwait(false);
            var filesWritten = writeResult.Files;

            var warnings = new List<string>(bundle.Warnings);
            if (analysis.StaticAnalysis is { } sa)
            {
                if (!string.IsNullOrWhiteSpace(sa.Summary))
                {
                    logger.LogInformation("Static analysis: {Summary}", sa.Summary);
                }

                warnings.AddRange(sa.Warnings.Take(5));
            }

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
                request.Progress?.Report("Updating meta and agent docs…");
                await WriteMetaAsync(request, analysis, bundle, filesWritten, changes, cancellationToken)
                    .ConfigureAwait(false);

                await ApplyAgentsAndReadmeAsync(
                        request,
                        analysis,
                        dryRun: false,
                        warnings,
                        cancellationToken)
                    .ConfigureAwait(false);

                await SaveLastRunAsync(request, analysis, bundle, filesWritten, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                warnings.Add(
                    $"[dry-run] Would create {writeResult.WouldCreate.Count}, " +
                    $"update {writeResult.WouldUpdate.Count}, " +
                    $"leave unchanged {writeResult.Unchanged.Count} wiki file(s).");
                await ApplyAgentsAndReadmeAsync(
                        request,
                        analysis,
                        dryRun: true,
                        warnings,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            sw.Stop();
            var mode = request.Incremental ? "update" : "generate";
            var source = bundle.UsedOfflineFallback ? "offline/multi-step" : "Semantic Kernel multi-step";
            var scopeLabel = scope.IsFull ? "full" : "selective";
            var modelName = request.ModelOverride
                            ?? LlmSettings.ResolveModel(request.Config);
            var cost = CostEstimator.Estimate(
                modelName,
                bundle.TokenUsage.InputTokens,
                bundle.TokenUsage.OutputTokens,
                request.Config);

            var writeSummary = request.DryRun
                ? $"dry-run plan: +{writeResult.WouldCreate.Count} create, ~{writeResult.WouldUpdate.Count} update, " +
                  $"{writeResult.Unchanged.Count} unchanged"
                : $"{filesWritten.Count} wiki files written";

            var message =
                $"{mode} ({scopeLabel}) complete for '{analysis.RepoName}' using {source}: " +
                $"{analysis.Stats.TotalFiles} files analyzed, {bundle.Modules.Count} modules, " +
                $"{writeSummary}. correlationId={request.CorrelationId}";

            if (bundle.TokenUsage.TotalTokens > 0)
            {
                message += $"; tokens in/out={bundle.TokenUsage.InputTokens}/{bundle.TokenUsage.OutputTokens}"
                           + $"; est. cost {cost.FormatUsd()} USD";
            }

            logger.LogInformation(
                "Run complete correlationId={CorrelationId} mode={Mode} durationMs={Ms} tokens={Tokens} costUsd={Cost} dryRun={DryRun} files={Files}",
                request.CorrelationId,
                mode,
                (int)sw.Elapsed.TotalMilliseconds,
                bundle.TokenUsage.TotalTokens,
                cost.EstimatedUsd,
                request.DryRun,
                filesWritten.Count);

            var result = GenerationResult.Ok(
                message: message,
                outputPath: request.OutputPath,
                filesWritten: filesWritten,
                duration: sw.Elapsed,
                warnings: warnings,
                analysis: analysis,
                inputTokens: bundle.TokenUsage.InputTokens,
                outputTokens: bundle.TokenUsage.OutputTokens,
                changeDetection: changes,
                costEstimate: cost,
                correlationId: request.CorrelationId,
                dryRun: request.DryRun,
                stepsCompleted: bundle.StepsCompleted,
                filesWouldCreate: writeResult.WouldCreate,
                filesWouldUpdate: writeResult.WouldUpdate,
                filesUnchanged: writeResult.Unchanged,
                moduleCount: bundle.Modules.Count,
                usedOfflineFallback: bundle.UsedOfflineFallback);

            try
            {
                runTelemetry.TrackRun(request, result);
            }
            catch (Exception telemetryEx)
            {
                logger.LogDebug(telemetryEx, "Telemetry track failed");
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Wiki generation failed (correlationId={CorrelationId})", request.CorrelationId);
            var fail = GenerationResult.Fail(ex.Message, sw.Elapsed, request.CorrelationId);
            try
            {
                runTelemetry.TrackRun(request, fail);
            }
            catch
            {
                // ignore telemetry failures
            }

            return fail;
        }
    }

    /// <summary>
    /// Full AGENTS.md when missing/trivial; bootstrap block when substantial;
    /// README when missing/generic. Respects config flags and dry-run.
    /// </summary>
    private async Task ApplyAgentsAndReadmeAsync(
        WikiGenerationRequest request,
        RepoAnalysisResult analysis,
        bool dryRun,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var agentPath = Path.IsPathRooted(request.Config.AgentMdPath)
            ? Path.GetFullPath(request.Config.AgentMdPath)
            : Path.GetFullPath(Path.Combine(request.RepoPath, request.Config.AgentMdPath));

        var trivialMax = request.Config.AgentsMdTrivialMaxLength > 0
            ? request.Config.AgentsMdTrivialMaxLength
            : Constants.Config.AgentsMdTrivialMaxLength;

        var needsFullAgents = request.Config.GenerateAgentsMdIfMissing
                              && AgentsMdFileClassifier.IsMissingOrTrivial(agentPath, trivialMax);

        if (needsFullAgents)
        {
            request.Progress?.Report("Creating full AGENTS.md…");
            var agentsResult = await agentsMdGenerator
                .GenerateAsync(
                    new AgentsMdGenerationRequest
                    {
                        Config = request.Config,
                        RepoPath = request.RepoPath,
                        WikiOutputPath = request.OutputPath,
                        Analysis = analysis,
                        Force = false,
                        DryRun = dryRun,
                        ModelOverride = request.ModelOverride,
                        ProviderOverride = request.ProviderOverride,
                        Progress = request.Progress
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (!agentsResult.Success)
            {
                warnings.Add($"Full AGENTS.md generation failed: {agentsResult.Error}");
            }
            else
            {
                warnings.Add(agentsResult.Message);
                warnings.AddRange(agentsResult.Warnings);
            }
        }
        else
        {
            request.Progress?.Report(dryRun
                ? "Checking AGENTS.md bootstrap block (dry-run)…"
                : "Updating AGENTS.md bootstrap block…");
            var bootstrap = await agentBootstrapper
                .EnsureInstructionsAsync(
                    request.RepoPath,
                    request.Config.AgentMdPath,
                    request.Config.OutputPath,
                    dryRun: dryRun,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!bootstrap.Success)
            {
                warnings.Add($"AGENTS.md bootstrap failed: {bootstrap.Error}");
            }
            else if (bootstrap.Action is BootstrapAction.Created or BootstrapAction.Updated
                     || dryRun)
            {
                warnings.Add($"Agent bootstrap: {bootstrap.Message}");
            }
        }

        if (request.Config.GenerateReadmeIfMissingOrGeneric)
        {
            request.Progress?.Report("Checking README.md…");
            var readmeResult = await readmeGenerator
                .GenerateAsync(
                    new ReadmeGenerationRequest
                    {
                        Config = request.Config,
                        RepoPath = request.RepoPath,
                        WikiOutputPath = request.OutputPath,
                        Analysis = analysis,
                        Force = false,
                        DryRun = dryRun,
                        Progress = request.Progress
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (!readmeResult.Success)
            {
                warnings.Add($"README generation failed: {readmeResult.Error}");
            }
            else if (readmeResult.Action is not ReadmeAction.Skipped)
            {
                warnings.Add(readmeResult.Message);
            }
            else if (dryRun)
            {
                warnings.Add(readmeResult.Message);
            }
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
            "api-endpoints.md",
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
            ToolVersion = Constants.Product.Version
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
        var metaPath = Path.Combine(request.OutputPath, Constants.Paths.MetaFileName);
        var meta = new
        {
            tool = Constants.Product.ToolName,
            version = Constants.Product.Version,
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
            estimatedUsd = CostEstimator.Estimate(
                request.ModelOverride ?? LlmSettings.ResolveModel(request.Config),
                bundle.TokenUsage,
                request.Config).EstimatedUsd,
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
