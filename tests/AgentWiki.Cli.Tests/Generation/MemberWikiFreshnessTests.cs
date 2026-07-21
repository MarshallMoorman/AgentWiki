using AgentWiki.Core;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;

namespace AgentWiki.Cli.Tests.Generation;

public sealed class MemberWikiFreshnessTests
{
    [Fact]
    public void Missing_WhenNoIndex()
    {
        var r = MemberWikiFreshness.Evaluate(
            hasWikiIndex: false,
            currentHeadSha: "abc",
            memberLastRunCommitSha: "abc",
            workspaceMemberBaselineSha: null);
        r.Kind.ShouldBe(MemberWikiFreshnessKind.Missing);
        r.Summary.ShouldBe("missing");
    }

    [Fact]
    public void Ok_WhenHeadMatchesBaseline_EvenIfOldFiles()
    {
        var r = MemberWikiFreshness.Evaluate(
            hasWikiIndex: true,
            currentHeadSha: "deadbeef",
            memberLastRunCommitSha: "deadbeef",
            workspaceMemberBaselineSha: null,
            calendarAgeWarningDays: 1,
            wikiLastWriteUtc: DateTimeOffset.UtcNow.AddDays(-100));
        r.Kind.ShouldBe(MemberWikiFreshnessKind.Ok);
        r.Summary.ShouldBe("ok");
        // Soft calendar warning only
        r.Warnings.ShouldContain(w => w.Contains("soft warning", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Stale_WhenNewCommitSinceBaseline()
    {
        var r = MemberWikiFreshness.Evaluate(
            hasWikiIndex: true,
            currentHeadSha: "newsha",
            memberLastRunCommitSha: "oldsha",
            workspaceMemberBaselineSha: null);
        r.Kind.ShouldBe(MemberWikiFreshnessKind.Stale);
        r.Summary.ShouldBe("stale");
    }

    [Fact]
    public void Stale_WhenNoBaseline_Conservative()
    {
        var r = MemberWikiFreshness.Evaluate(
            hasWikiIndex: true,
            currentHeadSha: "abc",
            memberLastRunCommitSha: null,
            workspaceMemberBaselineSha: null);
        r.Kind.ShouldBe(MemberWikiFreshnessKind.Stale);
    }

    [Fact]
    public void PreferMemberLastRunOverWorkspaceBaseline()
    {
        var r = MemberWikiFreshness.Evaluate(
            hasWikiIndex: true,
            currentHeadSha: "member-sha",
            memberLastRunCommitSha: "member-sha",
            workspaceMemberBaselineSha: "workspace-old");
        r.Kind.ShouldBe(MemberWikiFreshnessKind.Ok);
        r.BaselineSha.ShouldBe("member-sha");
    }

    [Fact]
    public void ShouldGenerate_Never_OnlyMissing()
    {
        MemberWikiFreshness.ShouldGenerate(
            MemberWikiFreshnessKind.Missing,
            ensureMissing: true,
            updateMembersPolicy: Constants.Workspace.UpdateMembersNever,
            forceAll: false).ShouldBeTrue();

        MemberWikiFreshness.ShouldGenerate(
            MemberWikiFreshnessKind.Stale,
            ensureMissing: true,
            updateMembersPolicy: Constants.Workspace.UpdateMembersNever,
            forceAll: false).ShouldBeFalse();

        MemberWikiFreshness.ShouldGenerate(
            MemberWikiFreshnessKind.Ok,
            ensureMissing: true,
            updateMembersPolicy: Constants.Workspace.UpdateMembersNever,
            forceAll: false).ShouldBeFalse();
    }

    [Fact]
    public void ShouldGenerate_Stale_IncludesStaleAndMissing()
    {
        MemberWikiFreshness.ShouldGenerate(
            MemberWikiFreshnessKind.Stale,
            ensureMissing: true,
            updateMembersPolicy: Constants.Workspace.UpdateMembersStale,
            forceAll: false).ShouldBeTrue();

        MemberWikiFreshness.ShouldGenerate(
            MemberWikiFreshnessKind.Missing,
            ensureMissing: true,
            updateMembersPolicy: Constants.Workspace.UpdateMembersStale,
            forceAll: false).ShouldBeTrue();

        MemberWikiFreshness.ShouldGenerate(
            MemberWikiFreshnessKind.Ok,
            ensureMissing: true,
            updateMembersPolicy: Constants.Workspace.UpdateMembersStale,
            forceAll: false).ShouldBeFalse();
    }

    [Fact]
    public void ShouldGenerate_All_ForcesOk()
    {
        MemberWikiFreshness.ShouldGenerate(
            MemberWikiFreshnessKind.Ok,
            ensureMissing: false,
            updateMembersPolicy: Constants.Workspace.UpdateMembersAll,
            forceAll: false).ShouldBeTrue();
    }

    [Fact]
    public void ShouldGenerate_ForceAll()
    {
        MemberWikiFreshness.ShouldGenerate(
            MemberWikiFreshnessKind.Ok,
            ensureMissing: false,
            updateMembersPolicy: Constants.Workspace.UpdateMembersNever,
            forceAll: true).ShouldBeTrue();
    }

    [Fact]
    public void ResolvePolicy_CliOverrides()
    {
        var config = new WorkspaceConfig
        {
            EnsureMemberWikis = true,
            MemberWikiPolicy = new MemberWikiPolicy
            {
                EnsureMissing = true,
                UpdateMembers = Constants.Workspace.UpdateMembersNever
            }
        };

        var (ensure, update) = MemberWikiFreshness.ResolvePolicy(
            config,
            ensureMissingOverride: false,
            updateMembersOverride: "stale");
        ensure.ShouldBeFalse();
        update.ShouldBe("stale");
    }

    [Fact]
    public void ResolvePolicy_LegacyFalseTightens()
    {
        var config = new WorkspaceConfig
        {
            EnsureMemberWikis = false,
            MemberWikiPolicy = new MemberWikiPolicy { EnsureMissing = true }
        };
        var (ensure, _) = MemberWikiFreshness.ResolvePolicy(config);
        ensure.ShouldBeFalse();
    }
}
