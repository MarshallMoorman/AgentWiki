using AgentWiki.Core;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.App.Services;

/// <summary>
/// Generates README.md when missing or generic. Conservative: never overwrites rich READMEs without force.
/// Uses a richer offline template (solution paths, wiki excerpts) and optional LLM polish.
/// </summary>
public sealed class ReadmeGenerator(
    IRepoAnalyzer repoAnalyzer,
    ILlmCompletionService llm,
    ILogger<ReadmeGenerator> logger) : IReadmeGenerator
{
    /// <inheritdoc />
    public async Task<ReadmeGenerationResult> GenerateAsync(
        ReadmeGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var repoPath = Path.GetFullPath(request.RepoPath);
            if (!Directory.Exists(repoPath))
            {
                return ReadmeGenerationResult.Fail($"Repository path does not exist: {repoPath}");
            }

            var target = Path.Combine(repoPath, Constants.Paths.DefaultReadmePath);
            var maxLen = request.Config.ReadmeGenericMaxLength > 0
                ? request.Config.ReadmeGenericMaxLength
                : Constants.Config.ReadmeGenericMaxLength;

            var missingOrGeneric = ReadmeHeuristics.IsMissingOrGeneric(target, maxLen);
            if (!request.Force && !missingOrGeneric)
            {
                return ReadmeGenerationResult.Skipped(
                    $"README.md at {target} looks project-specific; left unchanged.",
                    target);
            }

            request.Progress?.Report("Building README.md…");
            var analysis = request.Analysis
                           ?? await repoAnalyzer.AnalyzeAsync(repoPath, request.Config, cancellationToken)
                               .ConfigureAwait(false);

            var wikiRel = request.Config.OutputPath.Replace('\\', '/').Trim('/');
            var wikiAbs = request.WikiOutputPath
                          ?? Path.Combine(repoPath, wikiRel.Replace('/', Path.DirectorySeparatorChar));
            var wikiExists = Directory.Exists(wikiAbs)
                             && (File.Exists(Path.Combine(wikiAbs, "index.md"))
                                 || Directory.EnumerateFileSystemEntries(wikiAbs).Any());

            var excerpts = await LoadWikiExcerptsAsync(wikiAbs, cancellationToken).ConfigureAwait(false);
            var offline = ReadmeOfflineBuilder.Build(
                analysis,
                request.Config,
                wikiRel,
                wikiExists,
                excerpts);

            var content = offline;
            var offlineMode = LlmSettings.IsExplicitOfflineMode(request.Config.Provider);
            if (!offlineMode)
            {
                LlmSettings.EnsureLiveLlmConfigured(
                    request.Config,
                    providerOverride: null,
                    llm.CanUseLiveLlm(request.Config, null));

                try
                {
                    request.Progress?.Report("Enriching README.md with LLM…");
                    var polished = await TryLlmPolishAsync(request, offline, analysis, cancellationToken)
                        .ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(polished)
                        && polished.Contains(analysis.RepoName, StringComparison.OrdinalIgnoreCase)
                        && polished.Length > offline.Length / 2)
                    {
                        content = polished.TrimEnd() + Environment.NewLine;
                    }
                    else
                    {
                        logger.LogInformation(
                            "README LLM polish not applied (quality/shape); keeping offline template");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (!request.Config.AllowOfflineFallback)
                    {
                        logger.LogError(ex, "README LLM polish failed and AllowOfflineFallback=false");
                        throw;
                    }

                    logger.LogWarning(ex, "README LLM polish failed; using offline template");
                }
            }

            // Ensure agent docs pointer remains even if LLM omits it
            if (!content.Contains(Constants.Paths.DefaultAgentMdPath, StringComparison.OrdinalIgnoreCase))
            {
                content = content.TrimEnd()
                          + Environment.NewLine
                          + Environment.NewLine
                          + $"## Documentation for coding agents{Environment.NewLine}{Environment.NewLine}"
                          + $"- See [{Constants.Paths.DefaultAgentMdPath}]({Constants.Paths.DefaultAgentMdPath}) "
                          + $"and `{wikiRel}/` when present.{Environment.NewLine}";
            }

            var wasGeneric = File.Exists(target) && missingOrGeneric;
            var action = !File.Exists(target)
                ? ReadmeAction.Created
                : wasGeneric
                    ? ReadmeAction.ReplacedGeneric
                    : ReadmeAction.Created;

            if (request.DryRun)
            {
                var dry = wasGeneric
                    ? $"[dry-run] Would replace generic README.md at {target}"
                    : $"[dry-run] Would create README.md at {target}";
                logger.LogInformation("{Message}", dry);
                return ReadmeGenerationResult.Ok(dry, target, content, action, dryRun: true, wasGeneric: wasGeneric);
            }

            await File.WriteAllTextAsync(target, content, cancellationToken).ConfigureAwait(false);
            var message = wasGeneric
                ? $"Generated README.md (previous was generic template) at {target}"
                : $"Created README.md at {target}";
            logger.LogInformation("{Message}", message);
            return ReadmeGenerationResult.Ok(message, target, content, action, wasGeneric: wasGeneric);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "README generation failed for {Repo}", request.RepoPath);
            return ReadmeGenerationResult.Fail(ex.Message);
        }
    }

    private async Task<string?> TryLlmPolishAsync(
        ReadmeGenerationRequest request,
        string offlineDraft,
        RepoAnalysisResult analysis,
        CancellationToken cancellationToken)
    {
        var system =
            "You write clear, accurate README.md files for engineering teams and coding agents. "
            + "Return ONLY the full Markdown README. "
            + "Include: project purpose, build/test commands with real solution/project paths when known, "
            + "configuration notes, and links to AGENTS.md and docs/wiki when present. "
            + "Do not invent product claims, ports, or secrets. Prefer concrete paths from the draft.";

        var user = $"Repository: {analysis.RepoName}\n\nOffline draft to improve:\n\n{offlineDraft}";

        var result = await llm.CompleteAsync(
                request.Config,
                system,
                user,
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

        foreach (var (file, key) in new[] { ("architecture.md", "architecture"), ("index.md", "index") })
        {
            var path = Path.Combine(wikiAbs, file);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                map[key] = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        return map;
    }
}
