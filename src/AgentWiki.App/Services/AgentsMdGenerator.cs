using AgentWiki.Core;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.App.Services;

/// <summary>
/// Full AGENTS.md generator: offline template (+ optional LLM polish), instruction migration, dry-run safe.
/// </summary>
public sealed class AgentsMdGenerator(
    IRepoAnalyzer repoAnalyzer,
    IStaticAnalyzer staticAnalyzer,
    ILlmCompletionService llm,
    ILogger<AgentsMdGenerator> logger) : IAgentsMdGenerator
{
    /// <inheritdoc />
    public async Task<AgentsMdGenerationResult> GenerateAsync(
        AgentsMdGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var repoPath = Path.GetFullPath(request.RepoPath);
            if (!Directory.Exists(repoPath))
            {
                return AgentsMdGenerationResult.Fail($"Repository path does not exist: {repoPath}");
            }

            var target = ResolveAgentPath(repoPath, request.Config.AgentMdPath);
            var trivialMax = request.Config.AgentsMdTrivialMaxLength > 0
                ? request.Config.AgentsMdTrivialMaxLength
                : Constants.Config.AgentsMdTrivialMaxLength;

            if (!request.Force
                && File.Exists(target)
                && !AgentsMdFileClassifier.IsMissingOrTrivial(target, trivialMax))
            {
                return AgentsMdGenerationResult.Skipped(
                    $"Substantial AGENTS.md already exists at {target}. Use --force to overwrite, or rely on bootstrap block updates.",
                    target);
            }

            request.Progress?.Report("Analyzing repository for AGENTS.md…");
            var analysis = request.Analysis
                           ?? await repoAnalyzer.AnalyzeAsync(repoPath, request.Config, cancellationToken)
                               .ConfigureAwait(false);

            if (request.Config.EnableRoslynAnalysis && analysis.StaticAnalysis is null)
            {
                analysis.StaticAnalysis = await staticAnalyzer
                    .AnalyzeAsync(analysis, request.Config, cancellationToken)
                    .ConfigureAwait(false);
            }

            var wikiRel = request.Config.OutputPath.Replace('\\', '/').Trim('/');
            var wikiAbs = request.WikiOutputPath
                          ?? Path.Combine(repoPath, wikiRel.Replace('/', Path.DirectorySeparatorChar));
            var excerpts = await LoadWikiExcerptsAsync(wikiAbs, cancellationToken).ConfigureAwait(false);

            var instructions = request.Config.MigrateCopilotInstructions
                ? await InstructionFileDiscovery.DiscoverAsync(repoPath, logger, cancellationToken)
                    .ConfigureAwait(false)
                : [];

            request.Progress?.Report("Building full AGENTS.md…");
            var offline = AgentsMdOfflineBuilder.Build(
                analysis,
                request.Config,
                wikiRel,
                instructions,
                excerpts);

            var content = offline;
            var usedOffline = true;
            var warnings = new List<string>();

            if (llm.CanUseLiveLlm(request.Config, request.ProviderOverride))
            {
                try
                {
                    request.Progress?.Report("Enriching AGENTS.md with LLM…");
                    var enriched = await TryLlmEnrichAsync(
                            request,
                            offline,
                            analysis,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(enriched)
                        && enriched.Contains(Constants.AgentsMd.SelfUpdateSectionHeading, StringComparison.Ordinal)
                        && enriched.Contains(Constants.AgentsMd.MarkerBegin, StringComparison.Ordinal))
                    {
                        content = EnsureSelfUpdateAndTrailingNewline(enriched);
                        usedOffline = false;
                    }
                    else if (!string.IsNullOrWhiteSpace(enriched))
                    {
                        warnings.Add("LLM AGENTS.md output missing required sections; using offline template.");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "LLM AGENTS.md enrichment failed; using offline template");
                    warnings.Add($"LLM enrichment failed: {ex.Message}");
                }
            }

            // Always guarantee self-update section exists.
            if (!content.Contains(Constants.AgentsMd.SelfUpdateSectionHeading, StringComparison.Ordinal))
            {
                content = content.TrimEnd()
                          + Environment.NewLine
                          + Environment.NewLine
                          + AgentsMdOfflineBuilder.BuildSelfUpdateSectionMarkdown();
            }

            content = EnsureTrailingNewline(content);

            var action = File.Exists(target) ? AgentsMdAction.Updated : AgentsMdAction.Created;
            var migratedFrom = instructions.Select(i => i.RelativePath).ToList();
            var toDelete = instructions.Where(i => i.DeleteAfterMigration).Select(i => i.AbsolutePath).Distinct().ToList();
            var deleted = new List<string>();
            var wouldDelete = new List<string>();

            if (request.DryRun)
            {
                wouldDelete.AddRange(toDelete.Select(p => Path.GetRelativePath(repoPath, p).Replace('\\', '/')));
                var dryMsg =
                    $"[dry-run] Would {(action == AgentsMdAction.Created ? "create" : "overwrite")} full AGENTS.md at {target}"
                    + (wouldDelete.Count > 0
                        ? $"; would delete {wouldDelete.Count} migrated instruction file(s): {string.Join(", ", wouldDelete)}"
                        : "");
                logger.LogInformation("{Message}", dryMsg);
                return AgentsMdGenerationResult.Ok(
                    dryMsg,
                    target,
                    content,
                    action,
                    dryRun: true,
                    migratedFrom: migratedFrom,
                    wouldDelete: wouldDelete,
                    warnings: warnings,
                    usedOfflineFallback: usedOffline);
            }

            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.WriteAllTextAsync(target, content, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Wrote full AGENTS.md to {Path}", target);

            if (request.Config.MigrateCopilotInstructions && toDelete.Count > 0)
            {
                foreach (var file in toDelete)
                {
                    try
                    {
                        if (File.Exists(file))
                        {
                            File.Delete(file);
                            var rel = Path.GetRelativePath(repoPath, file).Replace('\\', '/');
                            deleted.Add(rel);
                            logger.LogInformation(
                                "Migrated content from {Path} and removed the file.",
                                rel);
                        }
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"Could not delete migrated instruction file {file}: {ex.Message}");
                        logger.LogWarning(ex, "Failed to delete {Path} after migration", file);
                    }
                }
            }

            var message = action == AgentsMdAction.Created
                ? $"Created full AGENTS.md at {target}"
                : $"Updated full AGENTS.md at {target}";
            if (deleted.Count > 0)
            {
                message += $". Migrated and removed: {string.Join(", ", deleted)}";
            }

            return AgentsMdGenerationResult.Ok(
                message,
                target,
                content,
                action,
                dryRun: false,
                migratedFrom: migratedFrom,
                deleted: deleted,
                warnings: warnings,
                usedOfflineFallback: usedOffline);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AGENTS.md generation failed for {Repo}", request.RepoPath);
            return AgentsMdGenerationResult.Fail(ex.Message);
        }
    }

    private async Task<string?> TryLlmEnrichAsync(
        AgentsMdGenerationRequest request,
        string offlineDraft,
        RepoAnalysisResult analysis,
        CancellationToken cancellationToken)
    {
        var system =
            "You improve AGENTS.md for coding agents. Return ONLY the full Markdown file. " +
            "You MUST keep a section headed exactly: "
            + Constants.AgentsMd.SelfUpdateSectionHeading
            + " with instructions to update AGENTS.md and README.md when agent-relevant workflows change. "
            + "You MUST keep the AgentWiki marker block "
            + Constants.AgentsMd.MarkerBegin + " … " + Constants.AgentsMd.MarkerEnd + ". "
            + "Be concrete, concise, and accurate to the inventory. Do not invent secrets.";

        var user =
            $"Repository: {analysis.RepoName}\n\n"
            + "Offline draft to refine:\n\n"
            + offlineDraft;

        var result = await llm.CompleteAsync(
                request.Config,
                system,
                user,
                modelOverride: request.ModelOverride,
                providerOverride: request.ProviderOverride,
                options: LlmRequestOptions.ConnectivityProbe,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.Content;
    }

    private static async Task<Dictionary<string, string>> LoadWikiExcerptsAsync(
        string wikiAbs,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(wikiAbs))
        {
            return map;
        }

        await TryReadAsync(wikiAbs, "index.md", "index", map, cancellationToken).ConfigureAwait(false);
        await TryReadAsync(wikiAbs, "architecture.md", "architecture", map, cancellationToken)
            .ConfigureAwait(false);
        return map;
    }

    private static async Task TryReadAsync(
        string wikiAbs,
        string fileName,
        string key,
        Dictionary<string, string> map,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(wikiAbs, fileName);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            map[key] = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // ignore missing/unreadable wiki pages
        }
    }

    private static string ResolveAgentPath(string repoPath, string agentMdPath)
    {
        if (Path.IsPathRooted(agentMdPath))
        {
            return Path.GetFullPath(agentMdPath);
        }

        var target = Path.GetFullPath(Path.Combine(repoPath, agentMdPath));
        if (IsDefaultAgentFile(agentMdPath)
            && !File.Exists(target)
            && File.Exists(Path.Combine(repoPath, Constants.Paths.DefaultClaudeMdPath)))
        {
            // Prefer writing AGENTS.md when neither exists; if only CLAUDE exists and is substantial,
            // caller should use bootstrap. For full generate with force/missing AGENTS, still write AGENTS.md.
            return target;
        }

        return target;
    }

    private static bool IsDefaultAgentFile(string agentMdPath) =>
        string.Equals(Path.GetFileName(agentMdPath), Constants.Paths.DefaultAgentMdPath, StringComparison.OrdinalIgnoreCase);

    private static string EnsureSelfUpdateAndTrailingNewline(string content)
    {
        if (!content.Contains(Constants.AgentsMd.SelfUpdateSectionHeading, StringComparison.Ordinal))
        {
            content = content.TrimEnd()
                      + Environment.NewLine
                      + Environment.NewLine
                      + AgentsMdOfflineBuilder.BuildSelfUpdateSectionMarkdown();
        }

        return EnsureTrailingNewline(content);
    }

    private static string EnsureTrailingNewline(string text) =>
        text.EndsWith('\n') ? text : text + Environment.NewLine;
}
