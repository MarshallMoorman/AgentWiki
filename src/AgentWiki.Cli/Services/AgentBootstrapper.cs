using System.Text;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Constants;
using Microsoft.Extensions.Logging;

namespace AgentWiki.Cli.Services;

/// <summary>
/// Idempotently creates/updates <c>AGENTS.md</c> (or configured path) with AgentWiki usage instructions.
/// </summary>
public sealed class AgentBootstrapper(ILogger<AgentBootstrapper> logger) : IAgentBootstrapper
{
    /// <inheritdoc />
    public async Task<AgentBootstrapResult> EnsureInstructionsAsync(
        string repoPath,
        string agentMdPath,
        string wikiOutputPathRelative,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(agentMdPath);

            var resolvedRepo = Path.GetFullPath(repoPath);
            var relativeWiki = wikiOutputPathRelative.Replace('\\', '/').TrimEnd('/') + "/";
            var target = Path.IsPathRooted(agentMdPath)
                ? Path.GetFullPath(agentMdPath)
                : Path.GetFullPath(Path.Combine(resolvedRepo, agentMdPath));

            // Prefer existing CLAUDE.md only when AgentMdPath is default and CLAUDE.md exists while AGENTS.md does not.
            if (IsDefaultAgentPath(agentMdPath)
                && !File.Exists(target)
                && File.Exists(Path.Combine(resolvedRepo, "CLAUDE.md")))
            {
                target = Path.Combine(resolvedRepo, "CLAUDE.md");
                logger.LogInformation("Using existing CLAUDE.md for agent bootstrap at {Path}", target);
            }

            var block = BuildBlock(relativeWiki);
            var existing = File.Exists(target)
                ? await File.ReadAllTextAsync(target, cancellationToken).ConfigureAwait(false)
                : null;

            if (existing is not null && ContainsAgentWikiBlock(existing))
            {
                var updated = ReplaceAgentWikiBlock(existing, block);
                if (string.Equals(Normalize(existing), Normalize(updated), StringComparison.Ordinal))
                {
                    logger.LogDebug("Agent bootstrap block already up to date at {Path}", target);
                    return AgentBootstrapResult.Ok("Agent instructions already present.", target, BootstrapAction.Unchanged);
                }

                if (!dryRun)
                {
                    await File.WriteAllTextAsync(target, EnsureTrailingNewline(updated), cancellationToken)
                        .ConfigureAwait(false);
                }

                logger.LogInformation("Updated AgentWiki block in {Path}", target);
                return AgentBootstrapResult.Ok(
                    dryRun ? $"[dry-run] Would update agent instructions in {target}" : $"Updated agent instructions in {target}",
                    target,
                    BootstrapAction.Updated);
            }

            var content = existing is null
                ? block
                : EnsureTrailingNewline(existing.TrimEnd()) + Environment.NewLine + Environment.NewLine + block;

            if (!dryRun)
            {
                var dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                await File.WriteAllTextAsync(target, EnsureTrailingNewline(content), cancellationToken)
                    .ConfigureAwait(false);
            }

            var action = existing is null ? BootstrapAction.Created : BootstrapAction.Updated;
            var verb = dryRun ? "[dry-run] Would write" : (action == BootstrapAction.Created ? "Created" : "Updated");
            logger.LogInformation("{Verb} agent bootstrap file {Path}", verb, target);
            return AgentBootstrapResult.Ok($"{verb} agent instructions at {target}", target, action);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent bootstrap failed for {RepoPath}", repoPath);
            return AgentBootstrapResult.Fail(ex.Message);
        }
    }

    internal static string BuildBlock(string wikiRelativePath)
    {
        var wiki = wikiRelativePath.Replace('\\', '/').TrimEnd('/') + "/";
        var sb = new StringBuilder();
        sb.AppendLine(AgentWikiConstants.AgentsMdMarkerBegin);
        sb.AppendLine("## AgentWiki Documentation");
        sb.AppendLine($"This repository maintains an **agent-optimized wiki** at `{wiki}`.");
        sb.AppendLine();
        sb.AppendLine("**For any task involving this codebase:**");
        sb.AppendLine($"1. Start by reading `{wiki}index.md` and `{wiki}architecture.md`");
        sb.AppendLine($"2. Drill into specific modules under `{wiki}modules/`");
        sb.AppendLine($"3. Review cross-cutting concerns under `{wiki}cross-cutting/` when relevant");
        sb.AppendLine("4. The wiki is kept up-to-date via `agent-wiki generate` / `update` (and CI when configured). Do not ignore it.");
        sb.AppendLine("5. Prefer wiki paths as a starting map, but always verify against source before making changes.");
        sb.AppendLine(AgentWikiConstants.AgentsMdMarkerEnd);
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    internal static bool ContainsAgentWikiBlock(string content) =>
        content.Contains(AgentWikiConstants.AgentsMdMarkerBegin, StringComparison.Ordinal)
        && content.Contains(AgentWikiConstants.AgentsMdMarkerEnd, StringComparison.Ordinal);

    internal static string ReplaceAgentWikiBlock(string content, string newBlock)
    {
        var start = content.IndexOf(AgentWikiConstants.AgentsMdMarkerBegin, StringComparison.Ordinal);
        var end = content.IndexOf(AgentWikiConstants.AgentsMdMarkerEnd, StringComparison.Ordinal);
        if (start < 0 || end < start)
        {
            return content.TrimEnd() + Environment.NewLine + Environment.NewLine + newBlock;
        }

        end += AgentWikiConstants.AgentsMdMarkerEnd.Length;
        // Consume a single trailing newline after the end marker if present.
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

    private static bool IsDefaultAgentPath(string agentMdPath) =>
        string.Equals(Path.GetFileName(agentMdPath), AgentWikiConstants.DefaultAgentMdPath, StringComparison.OrdinalIgnoreCase);

    private static string EnsureTrailingNewline(string text) =>
        text.EndsWith('\n') ? text : text + Environment.NewLine;

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";
}
