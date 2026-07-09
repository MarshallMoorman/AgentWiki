using System.Diagnostics;
using System.Text.Json;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Constants;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.Cli.Services;

/// <summary>
/// Phase 4 wiki generator: multi-step orchestration + AGENTS.md bootstrap.
/// </summary>
public sealed class SemanticWikiGenerator(
    IRepoAnalyzer repoAnalyzer,
    IWikiGenerationOrchestrator orchestrator,
    IOutputWriter outputWriter,
    IAgentBootstrapper agentBootstrapper,
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
                "Starting multi-step wiki generation for {RepoPath} (correlationId={CorrelationId}, dryRun={DryRun})",
                request.RepoPath,
                request.CorrelationId,
                request.DryRun);

            var analysis = await repoAnalyzer
                .AnalyzeAsync(request.RepoPath, request.Config, cancellationToken)
                .ConfigureAwait(false);

            var bundle = await orchestrator
                .GenerateAsync(analysis, request, cancellationToken)
                .ConfigureAwait(false);

            var filesWritten = await outputWriter
                .WriteAsync(request.OutputPath, bundle.Sections, request.DryRun, cancellationToken)
                .ConfigureAwait(false);

            var warnings = new List<string>(bundle.Warnings);
            AgentBootstrapResult? bootstrap = null;

            if (!request.DryRun)
            {
                await WriteMetaAsync(request, analysis, bundle, filesWritten, cancellationToken)
                    .ConfigureAwait(false);

                var wikiRelative = request.Config.OutputPath;
                bootstrap = await agentBootstrapper
                    .EnsureInstructionsAsync(
                        request.RepoPath,
                        request.Config.AgentMdPath,
                        wikiRelative,
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
            }
            else
            {
                bootstrap = await agentBootstrapper
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

            return GenerationResult.Ok(
                message:
                $"Phase 4 {mode} complete for '{analysis.RepoName}' using {source}: " +
                $"{analysis.Stats.TotalFiles} files, {bundle.Modules.Count} modules, " +
                $"{bundle.CrossCutting.Count} cross-cutting pages, {filesWritten.Count} wiki files written.",
                outputPath: request.OutputPath,
                filesWritten: filesWritten,
                duration: sw.Elapsed,
                warnings: warnings,
                analysis: analysis,
                inputTokens: bundle.TokenUsage.InputTokens,
                outputTokens: bundle.TokenUsage.OutputTokens);
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

    private async Task WriteMetaAsync(
        WikiGenerationRequest request,
        RepoAnalysisResult analysis,
        WikiBundle bundle,
        IReadOnlyList<string> filesWritten,
        CancellationToken cancellationToken)
    {
        var metaPath = Path.Combine(request.OutputPath, AgentWikiConstants.MetaFileName);
        var meta = new
        {
            tool = AgentWikiConstants.ToolName,
            version = AgentWikiConstants.Version,
            phase = 4,
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
