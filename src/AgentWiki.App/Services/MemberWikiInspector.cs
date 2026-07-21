using AgentWiki.Core;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;

namespace AgentWiki.App.Services;

/// <summary>
/// Checks whether a member repo has a usable wiki and git-based staleness (Step 02b).
/// </summary>
public sealed class MemberWikiInspector : IMemberWikiInspector
{
    /// <inheritdoc />
    public MemberWikiStatus Inspect(ResolvedWorkspaceMember member, int staleDays = 0)
    {
        // Sync path without baselines — uses git HEAD vs no baseline → often STALE.
        // Prefer InspectAsync / InspectWithBaselines for accurate orchestration.
        return InspectWithBaselines(
            member,
            memberLastRunCommitSha: null,
            workspaceMemberBaselineSha: null,
            calendarAgeWarningDays: staleDays > 0 ? staleDays : Constants.Config.MemberWikiStaleDays);
    }

    /// <summary>
    /// Inspect with explicit baselines (member last-run commit and/or workspace last-run head).
    /// </summary>
    public MemberWikiStatus InspectWithBaselines(
        ResolvedWorkspaceMember member,
        string? memberLastRunCommitSha,
        string? workspaceMemberBaselineSha,
        int calendarAgeWarningDays = 0)
    {
        ArgumentNullException.ThrowIfNull(member);

        if (calendarAgeWarningDays <= 0)
        {
            calendarAgeWarningDays = Constants.Config.MemberWikiStaleDays;
        }

        var wikiRel = member.Definition.WikiPath.Replace('\\', '/').Trim('/');
        var wikiAbs = Path.Combine(
            member.AbsolutePath,
            wikiRel.Replace('/', Path.DirectorySeparatorChar));
        var index = Path.Combine(wikiAbs, "index.md");
        var arch = Path.Combine(wikiAbs, "architecture.md");

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

        var freshness = MemberWikiFreshness.Evaluate(
            hasWikiIndex: hasIndex,
            currentHeadSha: member.HeadSha,
            memberLastRunCommitSha: memberLastRunCommitSha,
            workspaceMemberBaselineSha: workspaceMemberBaselineSha,
            calendarAgeWarningDays: calendarAgeWarningDays,
            wikiLastWriteUtc: lastWrite);

        var warnings = freshness.Warnings.ToList();
        if (!hasIndex)
        {
            warnings.Insert(
                0,
                $"Member '{member.Definition.Id}' wiki is missing ({wikiRel}/index.md). "
                + "Run `agent-wiki generate` in that repo or workspace generate with ensureMissing.");
        }

        return new MemberWikiStatus
        {
            MemberId = member.Definition.Id,
            WikiAbsolutePath = wikiAbs,
            Exists = exists && hasIndex,
            HasIndex = hasIndex,
            HasArchitecture = hasArchitecture,
            LastWriteUtc = lastWrite,
            IsStale = freshness.Kind == MemberWikiFreshnessKind.Stale,
            Summary = freshness.Summary,
            Warnings = warnings,
            Freshness = freshness.Kind.ToString(),
            BaselineSha = freshness.BaselineSha,
            CurrentHeadSha = freshness.CurrentHeadSha ?? member.HeadSha
        };
    }
}
