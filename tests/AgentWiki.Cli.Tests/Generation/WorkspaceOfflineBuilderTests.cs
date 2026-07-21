using AgentWiki.Core;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;

namespace AgentWiki.Cli.Tests.Generation;

public sealed class WorkspaceOfflineBuilderTests
{
    [Fact]
    public void BuildSections_ProducesCorpusLayoutAndRoutingCards()
    {
        var analysis = CreateSampleAnalysis();
        var sections = WorkspaceOfflineBuilder.BuildSections(analysis);

        sections.Select(s => s.RelativePath).ShouldContain("index.md");
        sections.Select(s => s.RelativePath).ShouldContain("architecture.md");
        sections.Select(s => s.RelativePath).ShouldContain("dependency-graph.md");
        sections.Select(s => s.RelativePath).ShouldContain("data-flows.md");
        sections.Select(s => s.RelativePath).ShouldContain("ownership.md");
        sections.Select(s => s.RelativePath).ShouldContain("routing-guide.md");
        sections.Select(s => s.RelativePath).ShouldContain("members/loan-service/index.md");
        sections.Select(s => s.RelativePath).ShouldContain("members/shared-domain/index.md");

        var index = sections.Single(s => s.RelativePath == "index.md").Content;
        index.ShouldContain("loan-service");
        index.ShouldContain("routing-guide");
        index.ShouldContain("experience");
        index.ShouldContain("Rise");
        index.ShouldContain("LoanView.Api");

        var card = sections.Single(s => s.RelativePath == "members/loan-service/index.md").Content;
        card.ShouldContain("Routing card");
        card.ShouldContain("experience");
        card.ShouldContain("Rise");
        card.ShouldContain("LoanView.Api");
        card.ShouldContain("github.com");
        card.ShouldContain("## Brands");
        card.ShouldContain("## Applications / Services");

        var guide = sections.Single(s => s.RelativePath == "routing-guide.md").Content;
        guide.ShouldContain("Candidate matrix");
        guide.ShouldContain("loan-service");

        var dep = sections.Single(s => s.RelativePath == "dependency-graph.md").Content;
        dep.ShouldContain("shared-domain");
    }

    [Fact]
    public void BuildRoutingCard_DoesNotInventManifestFields()
    {
        var member = CreateMember("bare", "Bare", "service", wikiExists: true, packages: []);
        member = new WorkspaceMemberAnalysis
        {
            Resolved = member.Resolved,
            WikiStatus = member.WikiStatus,
            Analysis = member.Analysis,
            Manifest = new WorkspaceManifestDocument { Present = false },
            Warnings = []
        };
        var analysis = new WorkspaceAnalysisResult
        {
            Config = new WorkspaceConfig { Name = "W", Members = [member.Resolved.Definition] },
            Members = [member],
            Signals = new CrossRepoSignals()
        };

        var card = WorkspaceOfflineBuilder.BuildRoutingCard(member, analysis);
        card.ShouldContain("_Not set in workspace-manifest.md_");
        card.ShouldNotContain("## Brands\n\nRise");
    }

    [Fact]
    public void BuildAgentsMd_IncludesSelfUpdateAndRouting()
    {
        var analysis = CreateSampleAnalysis();
        var agents = WorkspaceOfflineBuilder.BuildAgentsMd(analysis);

        agents.ShouldContain("Start here (workspace)");
        agents.ShouldContain(Constants.AgentsMd.MarkerBegin);
        agents.ShouldContain(Constants.AgentsMd.MarkerEnd);
        agents.ShouldContain(Constants.AgentsMd.SelfUpdateSectionHeading);
        agents.ShouldContain("docs/knowledge-base/");
        agents.ShouldContain("routing-guide");
        agents.ShouldContain("workspace generate");
        agents.ShouldContain("loan-service");
    }

    [Fact]
    public void DependencyGraph_ListsSharedPackages()
    {
        var analysis = CreateSampleAnalysis();
        var dep = WorkspaceOfflineBuilder.BuildSections(analysis)
            .Single(s => s.RelativePath == "dependency-graph.md")
            .Content;
        dep.ShouldContain("Newtonsoft.Json");
        dep.ShouldContain("loan-service");
        dep.ShouldContain("shared-domain");
    }

    private static WorkspaceAnalysisResult CreateSampleAnalysis()
    {
        var loan = CreateMember(
            "loan-service",
            "Loan Service",
            "service",
            wikiExists: true,
            packages: []);
        loan = WithManifest(loan, new WorkspaceManifestDocument
        {
            Present = true,
            Layer = "experience",
            Team = "@loan-team",
            Applications =
            [
                new WorkspaceManifestApplication
                {
                    Name = "LoanView.Api",
                    Description = "Experience API"
                }
            ],
            Brands = ["Rise", "Shine"],
            RouteWhen = ["Loan view stories"],
            Keywords = ["loan-view"]
        }, repoWeb: "https://github.com/org/LoanService", wikiWeb: "https://github.com/org/LoanService/blob/main/docs/wiki/index.md");

        var shared = CreateMember(
            "shared-domain",
            "Shared Domain",
            "library",
            wikiExists: true,
            packages: []);
        shared = WithManifest(shared, new WorkspaceManifestDocument
        {
            Present = true,
            Layer = "domain",
            Brands = ["Blueprint"],
            Applications = [new WorkspaceManifestApplication { Name = "Shared.Domain" }]
        });

        return new WorkspaceAnalysisResult
        {
            Config = new WorkspaceConfig
            {
                Name = "Lending Core",
                Description = "Core lending platform",
                OutputPath = "docs/knowledge-base",
                Members =
                [
                    loan.Resolved.Definition,
                    shared.Resolved.Definition
                ]
            },
            Members = [loan, shared],
            Signals = new CrossRepoSignals
            {
                SharedPackages =
                [
                    new PackageSignal
                    {
                        PackageId = "Newtonsoft.Json",
                        Ecosystem = "nuget",
                        MemberIds = ["loan-service", "shared-domain"],
                        Versions = ["13.0.3"]
                    }
                ]
            }
        };
    }

    private static WorkspaceMemberAnalysis WithManifest(
        WorkspaceMemberAnalysis baseMember,
        WorkspaceManifestDocument manifest,
        string? repoWeb = null,
        string? wikiWeb = null) =>
        new()
        {
            Resolved = baseMember.Resolved,
            WikiStatus = baseMember.WikiStatus,
            Analysis = baseMember.Analysis,
            Manifest = manifest,
            RepoWebUrl = repoWeb,
            WikiWebUrl = wikiWeb,
            Hosting = repoWeb?.Contains("github", StringComparison.OrdinalIgnoreCase) == true ? "github" : null,
            Warnings = []
        };

    private static WorkspaceMemberAnalysis CreateMember(
        string id,
        string label,
        string role,
        bool wikiExists,
        IReadOnlyList<string> packages)
    {
        _ = packages;
        var def = new WorkspaceMember
        {
            Id = id,
            Label = label,
            Role = role,
            Path = $"../{id}",
            WikiPath = "docs/wiki"
        };
        return new WorkspaceMemberAnalysis
        {
            Resolved = new ResolvedWorkspaceMember
            {
                Definition = def,
                AbsolutePath = $"/tmp/{id}",
                IsRemote = false,
                HeadSha = "abc123def456"
            },
            WikiStatus = new MemberWikiStatus
            {
                MemberId = id,
                WikiAbsolutePath = $"/tmp/{id}/docs/wiki",
                Exists = wikiExists,
                HasIndex = wikiExists,
                HasArchitecture = wikiExists,
                IsStale = false,
                Summary = wikiExists ? "ok" : "missing"
            },
            Analysis = new RepoAnalysisResult
            {
                RepoPath = $"/tmp/{id}",
                RepoName = id,
                Files = [],
                Summary = "test",
                DiscoveryMethod = "FileSystemWalk",
                Stats = new RepoStats
                {
                    TotalFiles = 10,
                    SelectedFiles = 5,
                    DetectedLanguages = ["C#"]
                }
            },
            Warnings = []
        };
    }
}
