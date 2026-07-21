using AgentWiki.Core.Models;

namespace AgentWiki.Core.Generation;

/// <summary>
/// Git-based member wiki freshness (Step 02b). Calendar age alone does not mark STALE.
/// </summary>
public enum MemberWikiFreshnessKind
{
    Missing,
    Stale,
    Ok
}

/// <summary>Computed freshness for orchestration and status.</summary>
public sealed class MemberWikiFreshnessResult
{
    public required MemberWikiFreshnessKind Kind { get; init; }
    public required string Summary { get; init; }
    public string? BaselineSha { get; init; }
    public string? CurrentHeadSha { get; init; }
    public bool HasIndex { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Evaluates MISSING / STALE / OK for a member wiki.
/// </summary>
public static class MemberWikiFreshness
{
    /// <summary>
    /// Evaluates freshness from wiki presence and git baseline.
    /// </summary>
    /// <param name="hasWikiIndex">True when <c>{wiki}/index.md</c> exists.</param>
    /// <param name="currentHeadSha">Current member HEAD (optional).</param>
    /// <param name="memberLastRunCommitSha">Commit from member <c>.agentwiki/last-run.json</c> when present.</param>
    /// <param name="workspaceMemberBaselineSha">Workspace last-run head for this member after a successful member generate.</param>
    /// <param name="calendarAgeWarningDays">Optional soft calendar warning only (does not mark stale).</param>
    /// <param name="wikiLastWriteUtc">Optional last wiki write for soft warning.</param>
    public static MemberWikiFreshnessResult Evaluate(
        bool hasWikiIndex,
        string? currentHeadSha,
        string? memberLastRunCommitSha,
        string? workspaceMemberBaselineSha,
        int calendarAgeWarningDays = 0,
        DateTimeOffset? wikiLastWriteUtc = null)
    {
        var warnings = new List<string>();

        if (!hasWikiIndex)
        {
            return new MemberWikiFreshnessResult
            {
                Kind = MemberWikiFreshnessKind.Missing,
                Summary = "missing",
                HasIndex = false,
                CurrentHeadSha = currentHeadSha,
                BaselineSha = PreferBaseline(memberLastRunCommitSha, workspaceMemberBaselineSha),
                Warnings = ["Member wiki index is missing."]
            };
        }

        var baseline = PreferBaseline(memberLastRunCommitSha, workspaceMemberBaselineSha);

        // Soft calendar warning only
        if (calendarAgeWarningDays > 0
            && wikiLastWriteUtc is not null
            && (DateTimeOffset.UtcNow - wikiLastWriteUtc.Value).TotalDays > calendarAgeWarningDays)
        {
            warnings.Add(
                $"Member wiki files are older than {calendarAgeWarningDays} days (soft warning only; not git-stale).");
        }

        if (string.IsNullOrWhiteSpace(baseline))
        {
            // Conservative: wiki exists but no baseline → STALE once so operators refresh under policy=stale.
            warnings.Add(
                "No git baseline for member wiki (no last-run commit / workspace head); treating as stale.");
            return new MemberWikiFreshnessResult
            {
                Kind = MemberWikiFreshnessKind.Stale,
                Summary = "stale",
                HasIndex = true,
                CurrentHeadSha = currentHeadSha,
                BaselineSha = null,
                Warnings = warnings
            };
        }

        if (string.IsNullOrWhiteSpace(currentHeadSha))
        {
            warnings.Add("Could not resolve current HEAD; treating as stale.");
            return new MemberWikiFreshnessResult
            {
                Kind = MemberWikiFreshnessKind.Stale,
                Summary = "stale",
                HasIndex = true,
                BaselineSha = baseline,
                Warnings = warnings
            };
        }

        if (!string.Equals(baseline.Trim(), currentHeadSha.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(
                $"Member git HEAD differs from wiki baseline ({Short(baseline)} → {Short(currentHeadSha)}).");
            return new MemberWikiFreshnessResult
            {
                Kind = MemberWikiFreshnessKind.Stale,
                Summary = "stale",
                HasIndex = true,
                BaselineSha = baseline,
                CurrentHeadSha = currentHeadSha,
                Warnings = warnings
            };
        }

        return new MemberWikiFreshnessResult
        {
            Kind = MemberWikiFreshnessKind.Ok,
            Summary = "ok",
            HasIndex = true,
            BaselineSha = baseline,
            CurrentHeadSha = currentHeadSha,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Whether orchestration should run member generate given policy.
    /// </summary>
    public static bool ShouldGenerate(
        MemberWikiFreshnessKind kind,
        bool ensureMissing,
        string updateMembersPolicy,
        bool forceAll)
    {
        if (forceAll)
        {
            return true;
        }

        var policy = (updateMembersPolicy ?? Constants.Workspace.DefaultUpdateMembers).Trim().ToLowerInvariant();

        if (kind == MemberWikiFreshnessKind.Missing)
        {
            return ensureMissing;
        }

        return policy switch
        {
            Constants.Workspace.UpdateMembersAll => true,
            Constants.Workspace.UpdateMembersStale => kind == MemberWikiFreshnessKind.Stale,
            _ => false // never
        };
    }

    /// <summary>
    /// Resolves effective ensureMissing / updateMembers with CLI &gt; policy &gt; legacy flag.
    /// </summary>
    /// <summary>
    /// Precedence: CLI overrides &gt; <see cref="MemberWikiPolicy"/> &gt; legacy <c>ensureMemberWikis</c>.
    /// </summary>
    public static (bool EnsureMissing, string UpdateMembers) ResolvePolicy(
        WorkspaceConfig config,
        bool? ensureMissingOverride = null,
        string? updateMembersOverride = null)
    {
        var policy = config.MemberWikiPolicy ?? new MemberWikiPolicy();

        // CLI wins; else policy.EnsureMissing; legacy EnsureMemberWikis can only tighten (AND).
        var ensureMissing = ensureMissingOverride
                            ?? (policy.EnsureMissing && config.EnsureMemberWikis);

        var update = !string.IsNullOrWhiteSpace(updateMembersOverride)
            ? updateMembersOverride.Trim().ToLowerInvariant()
            : (policy.UpdateMembers ?? Constants.Workspace.DefaultUpdateMembers).Trim().ToLowerInvariant();

        if (update is not (
            Constants.Workspace.UpdateMembersNever
            or Constants.Workspace.UpdateMembersStale
            or Constants.Workspace.UpdateMembersAll))
        {
            update = Constants.Workspace.DefaultUpdateMembers;
        }

        return (ensureMissing, update);
    }

    private static string? PreferBaseline(string? memberLastRun, string? workspaceBaseline) =>
        !string.IsNullOrWhiteSpace(memberLastRun)
            ? memberLastRun.Trim()
            : string.IsNullOrWhiteSpace(workspaceBaseline)
                ? null
                : workspaceBaseline.Trim();

    private static string Short(string sha) =>
        sha.Length <= 12 ? sha : sha[..12];
}
