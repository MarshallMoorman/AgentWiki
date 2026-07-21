using AgentWiki.Core;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Models;

namespace AgentWiki.App.Services;

/// <summary>
/// Checks whether a member repo has a usable wiki and whether it looks stale.
/// </summary>
public sealed class MemberWikiInspector : IMemberWikiInspector
{
    /// <inheritdoc />
    public MemberWikiStatus Inspect(ResolvedWorkspaceMember member, int staleDays = 0)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (staleDays <= 0)
        {
            staleDays = Constants.Config.MemberWikiStaleDays;
        }

        var wikiRel = member.Definition.WikiPath.Replace('\\', '/').Trim('/');
        var wikiAbs = Path.Combine(
            member.AbsolutePath,
            wikiRel.Replace('/', Path.DirectorySeparatorChar));
        var index = Path.Combine(wikiAbs, "index.md");
        var arch = Path.Combine(wikiAbs, "architecture.md");
        var warnings = new List<string>();

        var exists = Directory.Exists(wikiAbs);
        var hasIndex = File.Exists(index);
        var hasArchitecture = File.Exists(arch);
        DateTimeOffset? lastWrite = null;

        if (exists)
        {
            try
            {
                var newest = Directory
                    .EnumerateFiles(wikiAbs, "*.md", SearchOption.AllDirectories)
                    .Select(f => File.GetLastWriteTimeUtc(f))
                    .DefaultIfEmpty(DateTime.MinValue)
                    .Max();
                if (newest > DateTime.MinValue)
                {
                    lastWrite = new DateTimeOffset(DateTime.SpecifyKind(newest, DateTimeKind.Utc));
                }
            }
            catch
            {
                // ignore
            }
        }

        var isStale = false;
        if (!hasIndex)
        {
            warnings.Add(
                $"Member '{member.Definition.Id}' wiki is missing ({wikiRel}/index.md). "
                + $"Run `agent-wiki generate` in that repo first.");
        }
        else if (lastWrite is not null)
        {
            var age = DateTimeOffset.UtcNow - lastWrite.Value;
            if (age.TotalDays > staleDays)
            {
                isStale = true;
                warnings.Add(
                    $"Member '{member.Definition.Id}' wiki looks stale "
                    + $"(last write {lastWrite:u}, threshold {staleDays} days). "
                    + "Consider `agent-wiki update` in that repo.");
            }
        }

        var summary = !exists
            ? "missing"
            : !hasIndex
                ? "incomplete"
                : isStale
                    ? "stale"
                    : "ok";

        return new MemberWikiStatus
        {
            MemberId = member.Definition.Id,
            WikiAbsolutePath = wikiAbs,
            Exists = exists && hasIndex,
            HasIndex = hasIndex,
            HasArchitecture = hasArchitecture,
            LastWriteUtc = lastWrite,
            IsStale = isStale,
            Summary = summary,
            Warnings = warnings
        };
    }
}
