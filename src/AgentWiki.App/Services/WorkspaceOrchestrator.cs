using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AgentWiki.Core;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.App.Services;

/// <summary>
/// File-based multi-repo workspace orchestrator (Phase 1).
/// Reuses single-repo <see cref="IWikiGenerator"/> for member wikis and
/// <see cref="WorkspaceOfflineBuilder"/> for system pages.
/// </summary>
public sealed class WorkspaceOrchestrator(
    IWorkspaceConfigLoader workspaceConfigLoader,
    IWorkspaceMemberResolver memberResolver,
    IMemberWikiInspector wikiInspector,
    ICrossRepoSignalCollector signalCollector,
    IRepoAnalyzer repoAnalyzer,
    IConfigLoader configLoader,
    IWikiGenerator wikiGenerator,
    IOutputWriter outputWriter,
    IWorkspaceLastRunStore lastRunStore,
    ILogger<WorkspaceOrchestrator> logger) : IWorkspaceOrchestrator
{
    private static readonly JsonSerializerOptions MetaJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <inheritdoc />
    public async Task<WorkspaceGenerationResult> GenerateAsync(
        WorkspaceGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sw = Stopwatch.StartNew();
        var steps = new List<string>();
        var warnings = new List<string>();

        try
        {
            var root = PathUtility.ExpandAndResolve(request.WorkspaceRoot);
            if (!Directory.Exists(root))
            {
                return WorkspaceGenerationResult.Fail(
                    $"Workspace root does not exist: {root}",
                    sw.Elapsed,
                    request.CorrelationId);
            }

            var config = request.Config;
            config.WorkspaceRoot = root;

            logger.LogInformation(
                "Starting workspace generation '{Name}' (correlationId={Id}, incremental={Inc}, dryRun={Dry})",
                config.Name,
                request.CorrelationId,
                request.Incremental,
                request.DryRun);

            request.Progress?.Report("Resolving workspace members…");
            var resolved = await memberResolver.ResolveAllAsync(config, cancellationToken).ConfigureAwait(false);
            steps.Add("resolve-members");

            foreach (var r in resolved.Where(x => !x.Success))
            {
                warnings.Add(r.Error ?? $"Member '{r.Definition.Id}' failed to resolve.");
            }

            var successful = resolved.Where(r => r.Success).ToList();
            if (successful.Count == 0)
            {
                return WorkspaceGenerationResult.Fail(
                    "No workspace members could be resolved. Fix member paths/remotes and retry.",
                    sw.Elapsed,
                    request.CorrelationId,
                    warnings);
            }

            // Incremental: load last-run and decide which members need work.
            WorkspaceLastRunState? lastRun = null;
            if (request.Incremental)
            {
                request.Progress?.Report("Loading workspace last-run state…");
                lastRun = await lastRunStore.LoadAsync(root, cancellationToken).ConfigureAwait(false);
                steps.Add("load-last-run");
            }

            var memberAnalyses = new List<WorkspaceMemberAnalysis>();
            var membersGenerated = 0;

            foreach (var member in resolved)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var memberWarnings = new List<string>(member.Warnings);

                if (!member.Success)
                {
                    memberAnalyses.Add(new WorkspaceMemberAnalysis
                    {
                        Resolved = member,
                        Warnings = memberWarnings
                    });
                    continue;
                }

                request.Progress?.Report($"Inspecting member '{member.Definition.Id}'…");
                var wikiStatus = wikiInspector.Inspect(member);
                memberWarnings.AddRange(wikiStatus.Warnings);

                GenerationResult? genResult = null;
                RepoAnalysisResult? analysis = null;

                var needsMemberWiki =
                    config.EnsureMemberWikis
                    && (!wikiStatus.Exists || wikiStatus.IsStale || request.Force);

                if (request.Incremental && lastRun is not null && !request.Force)
                {
                    needsMemberWiki = ShouldRegenerateMember(member, wikiStatus, lastRun) && config.EnsureMemberWikis;
                    if (!needsMemberWiki && wikiStatus.Exists)
                    {
                        logger.LogDebug(
                            "Member {Id} unchanged since last workspace run — skipping member wiki generate",
                            member.Definition.Id);
                    }
                }

                if (needsMemberWiki)
                {
                    request.Progress?.Report(
                        wikiStatus.Exists
                            ? $"Updating member wiki '{member.Definition.Id}'…"
                            : $"Generating member wiki '{member.Definition.Id}'…");

                    genResult = await GenerateMemberWikiAsync(
                            member,
                            request,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (genResult.Success)
                    {
                        membersGenerated++;
                        // Refresh status after generation
                        wikiStatus = wikiInspector.Inspect(member);
                    }
                    else
                    {
                        memberWarnings.Add(
                            $"Member '{member.Definition.Id}' wiki generation failed: {genResult.Error ?? genResult.Message}. "
                            + $"Run `agent-wiki generate --repo-path {member.AbsolutePath}` manually.");
                        warnings.Add(memberWarnings[^1]);
                    }
                }
                else if (!wikiStatus.Exists)
                {
                    var msg =
                        $"Member '{member.Definition.Id}' wiki is missing — run `agent-wiki generate` in that repo "
                        + "or enable ensureMemberWikis and re-run workspace generate.";
                    memberWarnings.Add(msg);
                    warnings.Add(msg);
                }

                // Always analyze inventory for system pages (offline-safe).
                request.Progress?.Report($"Analyzing inventory for '{member.Definition.Id}'…");
                try
                {
                    var memberConfig = await configLoader
                        .LoadAsync(member.AbsolutePath, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    // Apply workspace ignore extras if any.
                    if (config.IgnorePatterns.Count > 0)
                    {
                        foreach (var p in config.IgnorePatterns)
                        {
                            if (!memberConfig.IgnorePatterns.Contains(p, StringComparer.OrdinalIgnoreCase))
                            {
                                memberConfig.IgnorePatterns.Add(p);
                            }
                        }
                    }

                    analysis = await repoAnalyzer
                        .AnalyzeAsync(member.AbsolutePath, memberConfig, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    memberWarnings.Add($"Inventory for '{member.Definition.Id}' failed: {ex.Message}");
                    logger.LogWarning(ex, "Failed to analyze member {Id}", member.Definition.Id);
                }

                memberAnalyses.Add(new WorkspaceMemberAnalysis
                {
                    Resolved = member,
                    WikiStatus = wikiStatus,
                    Analysis = analysis,
                    MemberGenerateResult = genResult,
                    Warnings = memberWarnings
                });
            }

            steps.Add("member-wikis-and-inventory");

            // Incremental short-circuit: if nothing changed and system output exists, skip rewrite.
            if (request.Incremental
                && !request.Force
                && lastRun is not null
                && membersGenerated == 0
                && !MembersChanged(memberAnalyses, lastRun)
                && Directory.Exists(request.OutputPath)
                && File.Exists(Path.Combine(request.OutputPath, "index.md")))
            {
                sw.Stop();
                return WorkspaceGenerationResult.Ok(
                    "No workspace changes detected since last run.",
                    request.OutputPath,
                    [],
                    sw.Elapsed,
                    warnings,
                    request.CorrelationId,
                    request.DryRun,
                    steps,
                    memberCount: successful.Count,
                    membersGenerated: 0);
            }

            request.Progress?.Report("Collecting cross-repo signals…");
            var signals = await signalCollector
                .CollectAsync(memberAnalyses, cancellationToken)
                .ConfigureAwait(false);
            steps.Add("cross-repo-signals");

            var analysisResult = new WorkspaceAnalysisResult
            {
                Config = config,
                Members = memberAnalyses,
                Signals = signals,
                Warnings = warnings,
                Duration = sw.Elapsed
            };

            request.Progress?.Report("Building system knowledge base pages…");
            var sections = WorkspaceOfflineBuilder.BuildSections(analysisResult).ToList();
            steps.Add("build-system-pages");

            // Meta file
            sections.Add(new WikiSection(
                "meta",
                "Meta",
                Constants.Paths.MetaFileName,
                JsonSerializer.Serialize(
                    new
                    {
                        tool = Constants.Product.ToolName,
                        version = Constants.Product.Version,
                        mode = request.Incremental ? "workspace-update" : "workspace-generate",
                        workspace = config.Name,
                        correlationId = request.CorrelationId,
                        generatedAtUtc = DateTimeOffset.UtcNow,
                        memberCount = successful.Count,
                        members = successful.Select(m => m.Definition.Id).ToList()
                    },
                    MetaJsonOptions)));

            request.Progress?.Report(
                request.DryRun ? "Dry-run: classifying system wiki files…" : "Writing system wiki…");
            var writeResult = await outputWriter
                .WriteAsync(request.OutputPath, sections, request.DryRun, cancellationToken)
                .ConfigureAwait(false);
            steps.Add("write-system-wiki");

            var filesWritten = writeResult.Files.ToList();
            var wouldCreate = writeResult.WouldCreate.ToList();
            var wouldUpdate = writeResult.WouldUpdate.ToList();
            var unchanged = writeResult.Unchanged.ToList();

            if (config.GenerateAgentsMd)
            {
                request.Progress?.Report(
                    request.DryRun ? "Dry-run: workspace AGENTS.md…" : "Writing workspace AGENTS.md…");
                var agentsOutcome = await WriteWorkspaceAgentsMdAsync(
                        root,
                        config,
                        analysisResult,
                        request.DryRun,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (agentsOutcome is not null)
                {
                    filesWritten.Add(agentsOutcome);
                    if (request.DryRun)
                    {
                        wouldCreate.Add(agentsOutcome);
                    }
                }

                steps.Add("workspace-agents-md");
            }

            if (!request.DryRun)
            {
                request.Progress?.Report("Saving workspace last-run state…");
                await lastRunStore
                    .SaveAsync(root, BuildLastRunState(request, config, memberAnalyses, filesWritten), cancellationToken)
                    .ConfigureAwait(false);
                steps.Add("save-last-run");
            }

            sw.Stop();
            var message = request.DryRun
                ? $"Dry-run complete for workspace '{config.Name}' ({successful.Count} members, {filesWritten.Count} system pages planned)."
                : $"Workspace system wiki generated for '{config.Name}' ({successful.Count} members, {filesWritten.Count} files, {membersGenerated} member wiki(s) refreshed).";

            logger.LogInformation("{Message} duration={Duration}ms", message, sw.ElapsedMilliseconds);

            return WorkspaceGenerationResult.Ok(
                message,
                request.OutputPath,
                filesWritten,
                sw.Elapsed,
                warnings,
                request.CorrelationId,
                request.DryRun,
                steps,
                wouldCreate,
                wouldUpdate,
                unchanged,
                successful.Count,
                membersGenerated,
                analysisResult,
                usedOfflineFallback: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Workspace generation failed");
            return WorkspaceGenerationResult.Fail(ex.Message, sw.Elapsed, request.CorrelationId, warnings);
        }
    }

    /// <inheritdoc />
    public async Task<WorkspaceStatusResult> GetStatusAsync(
        string workspaceRoot,
        string? workspaceConfigPath = null,
        CancellationToken cancellationToken = default)
    {
        var load = await workspaceConfigLoader
            .LoadAsync(workspaceRoot, workspaceConfigPath, cancellationToken)
            .ConfigureAwait(false);
        if (!load.Success || load.Config is null)
        {
            return new WorkspaceStatusResult
            {
                Success = false,
                Error = load.Error
            };
        }

        var config = load.Config;
        var resolved = await memberResolver.ResolveAllAsync(config, cancellationToken).ConfigureAwait(false);
        var wikiStatuses = resolved
            .Where(r => r.Success)
            .Select(r => wikiInspector.Inspect(r))
            .ToList();
        var lastRun = await lastRunStore.LoadAsync(config.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
        var warnings = new List<string>(load.Warnings);
        warnings.AddRange(resolved.Where(r => !r.Success).Select(r => r.Error!));
        warnings.AddRange(wikiStatuses.SelectMany(w => w.Warnings));

        return new WorkspaceStatusResult
        {
            Success = true,
            Config = config,
            LastRun = lastRun,
            ResolvedMembers = resolved,
            WikiStatuses = wikiStatuses,
            Warnings = warnings
        };
    }

    private async Task<GenerationResult> GenerateMemberWikiAsync(
        ResolvedWorkspaceMember member,
        WorkspaceGenerationRequest request,
        CancellationToken cancellationToken)
    {
        var memberConfig = await configLoader
            .LoadAsync(member.AbsolutePath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(request.ModelOverride))
        {
            memberConfig.DefaultModel = request.ModelOverride;
        }

        if (!string.IsNullOrWhiteSpace(request.ProviderOverride))
        {
            memberConfig.Provider = request.ProviderOverride;
        }

        // Prefer offline for remote/cache members unless provider already set for live use.
        // Keep member config as-is so credentials from member/.env work when present.

        var wikiRel = member.Definition.WikiPath;
        var outputPath = Path.IsPathRooted(PathUtility.ExpandHome(wikiRel))
            ? PathUtility.ExpandAndResolve(wikiRel)
            : Path.GetFullPath(Path.Combine(member.AbsolutePath, wikiRel));

        var genRequest = new WikiGenerationRequest
        {
            Config = memberConfig,
            RepoPath = member.AbsolutePath,
            OutputPath = outputPath,
            Force = request.Force || !Directory.Exists(outputPath),
            DryRun = request.DryRun,
            Incremental = request.Incremental && Directory.Exists(outputPath),
            ModelOverride = request.ModelOverride,
            ProviderOverride = request.ProviderOverride,
            CorrelationId = request.CorrelationId,
            Progress = request.Progress is null
                ? null
                : new Progress<string>(msg => request.Progress.Report($"[{member.Definition.Id}] {msg}"))
        };

        return await wikiGenerator.GenerateAsync(genRequest, cancellationToken).ConfigureAwait(false);
    }

    private static bool ShouldRegenerateMember(
        ResolvedWorkspaceMember member,
        MemberWikiStatus wikiStatus,
        WorkspaceLastRunState lastRun)
    {
        if (!wikiStatus.Exists || wikiStatus.IsStale)
        {
            return true;
        }

        if (!lastRun.Members.TryGetValue(member.Definition.Id, out var prior))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(member.HeadSha)
            && !string.IsNullOrWhiteSpace(prior.HeadSha)
            && !string.Equals(member.HeadSha, prior.HeadSha, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (wikiStatus.LastWriteUtc is not null
            && prior.WikiLastWriteUtc is not null
            && wikiStatus.LastWriteUtc > prior.WikiLastWriteUtc)
        {
            // Member wiki was refreshed outside workspace — no need to regenerate member, but system may need update.
            return false;
        }

        return false;
    }

    private static bool MembersChanged(
        IReadOnlyList<WorkspaceMemberAnalysis> members,
        WorkspaceLastRunState lastRun)
    {
        var currentIds = members
            .Where(m => m.Resolved.Success)
            .Select(m => m.Resolved.Definition.Id)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var priorIds = lastRun.Members.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        if (!currentIds.SequenceEqual(priorIds, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var m in members.Where(x => x.Resolved.Success))
        {
            if (!lastRun.Members.TryGetValue(m.Resolved.Definition.Id, out var prior))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(m.Resolved.HeadSha)
                && !string.IsNullOrWhiteSpace(prior.HeadSha)
                && !string.Equals(m.Resolved.HeadSha, prior.HeadSha, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static WorkspaceLastRunState BuildLastRunState(
        WorkspaceGenerationRequest request,
        WorkspaceConfig config,
        IReadOnlyList<WorkspaceMemberAnalysis> members,
        IReadOnlyList<string> filesWritten)
    {
        var state = new WorkspaceLastRunState
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            CorrelationId = request.CorrelationId,
            Mode = request.Incremental ? "update" : "generate",
            OutputPath = request.OutputPath,
            WorkspaceName = config.Name,
            FilesWritten = filesWritten,
            ToolVersion = Constants.Product.Version,
            Members = new Dictionary<string, WorkspaceMemberLastRun>(StringComparer.OrdinalIgnoreCase)
        };

        foreach (var m in members.Where(x => x.Resolved.Success))
        {
            state.Members[m.Resolved.Definition.Id] = new WorkspaceMemberLastRun
            {
                HeadSha = m.Resolved.HeadSha,
                WikiLastWriteUtc = m.WikiStatus?.LastWriteUtc,
                TimestampUtc = DateTimeOffset.UtcNow,
                WikiExisted = m.WikiStatus?.Exists ?? false
            };
        }

        return state;
    }

    private async Task<string?> WriteWorkspaceAgentsMdAsync(
        string workspaceRoot,
        WorkspaceConfig config,
        WorkspaceAnalysisResult analysis,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var relative = string.IsNullOrWhiteSpace(config.AgentMdPath)
            ? Constants.Paths.DefaultAgentMdPath
            : config.AgentMdPath;
        var target = Path.IsPathRooted(PathUtility.ExpandHome(relative))
            ? PathUtility.ExpandAndResolve(relative)
            : Path.GetFullPath(Path.Combine(workspaceRoot, relative));

        var fullContent = WorkspaceOfflineBuilder.BuildAgentsMd(analysis);
        var block = WorkspaceOfflineBuilder.BuildWorkspaceBootstrapBlock(config.OutputPath);

        string? existing = File.Exists(target)
            ? await File.ReadAllTextAsync(target, cancellationToken).ConfigureAwait(false)
            : null;

        string toWrite;
        if (existing is null || IsTrivialAgents(existing))
        {
            toWrite = fullContent;
        }
        else if (ContainsMarker(existing))
        {
            toWrite = ReplaceMarkerBlock(existing, block);
            // Ensure self-update section exists
            if (!existing.Contains(Constants.AgentsMd.SelfUpdateSectionHeading, StringComparison.Ordinal))
            {
                var sb = new StringBuilder(toWrite.TrimEnd());
                sb.AppendLine();
                sb.AppendLine();
                WorkspaceOfflineBuilder.AppendWorkspaceSelfUpdateSection(sb);
                toWrite = sb.ToString();
                if (!toWrite.EndsWith('\n'))
                {
                    toWrite += Environment.NewLine;
                }
            }
        }
        else
        {
            // Rich file without markers — append block + self-update guidance.
            var sb = new StringBuilder(existing.TrimEnd());
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(block.TrimEnd());
            sb.AppendLine();
            sb.AppendLine();
            WorkspaceOfflineBuilder.AppendWorkspaceSelfUpdateSection(sb);
            toWrite = sb.ToString();
            if (!toWrite.EndsWith('\n'))
            {
                toWrite += Environment.NewLine;
            }
        }

        if (dryRun)
        {
            logger.LogInformation("[dry-run] Would write workspace AGENTS.md at {Path}", target);
            return Path.GetRelativePath(workspaceRoot, target).Replace('\\', '/');
        }

        var dir = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllTextAsync(target, toWrite, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Wrote workspace AGENTS.md at {Path}", target);
        return Path.GetRelativePath(workspaceRoot, target).Replace('\\', '/');
    }

    private static bool IsTrivialAgents(string content) =>
        content.Trim().Length < Constants.Config.AgentsMdTrivialMaxLength;

    private static bool ContainsMarker(string content) =>
        content.Contains(Constants.AgentsMd.MarkerBegin, StringComparison.Ordinal)
        && content.Contains(Constants.AgentsMd.MarkerEnd, StringComparison.Ordinal);

    private static string ReplaceMarkerBlock(string content, string newBlock)
    {
        var start = content.IndexOf(Constants.AgentsMd.MarkerBegin, StringComparison.Ordinal);
        var end = content.IndexOf(Constants.AgentsMd.MarkerEnd, StringComparison.Ordinal);
        if (start < 0 || end < start)
        {
            return content.TrimEnd() + Environment.NewLine + Environment.NewLine + newBlock;
        }

        end += Constants.AgentsMd.MarkerEnd.Length;
        if (end < content.Length && content[end] == '\r')
        {
            end++;
        }

        if (end < content.Length && content[end] == '\n')
        {
            end++;
        }

        return content[..start] + newBlock.TrimEnd() + Environment.NewLine + content[end..];
    }
}
