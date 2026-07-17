using AgentWiki.Core;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.App.Services;

/// <summary>
/// Generates README.md when missing or generic. Conservative: never overwrites rich READMEs without force.
/// </summary>
public sealed class ReadmeGenerator(
    IRepoAnalyzer repoAnalyzer,
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

            var content = ReadmeOfflineBuilder.Build(analysis, request.Config, wikiRel, wikiExists);
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
}
