using AgentWiki.Core;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;

namespace AgentWiki.Cli.Tests.Generation;

public sealed class WorkspaceOfflineBuilderTests
{
    [Fact]
    public void BuildSections_ProducesExpectedPagesAndMemberLinks()
    {
        var analysis = CreateSampleAnalysis();
        var sections = WorkspaceOfflineBuilder.BuildSections(analysis);

        sections.Select(s => s.RelativePath).ShouldContain("index.md");
        sections.Select(s => s.RelativePath).ShouldContain("architecture.md");
        sections.Select(s => s.RelativePath).ShouldContain("dependency-graph.md");
        sections.Select(s => s.RelativePath).ShouldContain("data-flows.md");
        sections.Select(s => s.RelativePath).ShouldContain("ownership.md");
        sections.Select(s => s.RelativePath).ShouldContain("members/loan-service.md");
        sections.Select(s => s.RelativePath).ShouldContain("members/shared-domain.md");

        var index = sections.Single(s => s.RelativePath == "index.md").Content;
        index.ShouldContain("loan-service");
        index.ShouldContain("docs/wiki/index.md");

        var member = sections.Single(s => s.RelativePath == "members/loan-service.md").Content;
        member.ShouldContain("docs/wiki/architecture.md");
        member.ShouldContain("loan-service");

        var dep = sections.Single(s => s.RelativePath == "dependency-graph.md").Content;
        dep.ShouldContain("shared-domain");
    }

    [Fact]
    public void BuildAgentsMd_IncludesSelfUpdateAndMarkers()
    {
        var analysis = CreateSampleAnalysis();
        var agents = WorkspaceOfflineBuilder.BuildAgentsMd(analysis);

        agents.ShouldContain("Start here (workspace)");
        agents.ShouldContain(Constants.AgentsMd.MarkerBegin);
        agents.ShouldContain(Constants.AgentsMd.MarkerEnd);
        agents.ShouldContain(Constants.AgentsMd.SelfUpdateSectionHeading);
        agents.ShouldContain("docs/knowledge-base/");
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
        var shared = CreateMember(
            "shared-domain",
            "Shared Domain",
            "library",
            wikiExists: true,
            packages: []);

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
                ],
                ProjectReferences =
                [
                    new ProjectReferenceSignal
                    {
                        FromMemberId = "loan-service",
                        FromProject = "src/Loan.csproj",
                        ToReference = "../Shared.Domain/Shared.Domain.csproj",
                        MatchedMemberId = "shared-domain"
                    }
                ],
                Ownership =
                [
                    new OwnershipSignal
                    {
                        MemberId = "loan-service",
                        SourcePath = "CODEOWNERS",
                        Excerpt = "* @lending-team"
                    }
                ],
                Contracts =
                [
                    new ContractSignal
                    {
                        MemberId = "loan-service",
                        RelativePath = "openapi/loan.json",
                        Kind = "openapi"
                    }
                ],
                Notes = ["Detected 1 package(s) shared by 2+ members."]
            },
            Warnings = []
        };
    }

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
                HeadSha = "abc123"
            },
            WikiStatus = new MemberWikiStatus
            {
                MemberId = id,
                WikiAbsolutePath = $"/tmp/{id}/docs/wiki",
                Exists = wikiExists,
                HasIndex = wikiExists,
                HasArchitecture = wikiExists,
                LastWriteUtc = DateTimeOffset.UtcNow,
                IsStale = false,
                Summary = wikiExists ? "ok" : "missing"
            },
            Analysis = new RepoAnalysisResult
            {
                RepoPath = $"/tmp/{id}",
                RepoName = label,
                Files =
                [
                    new RepoFile
                    {
                        RelativePath = $"src/{label.Replace(" ", "")}.csproj",
                        AbsolutePath = $"/tmp/{id}/src/x.csproj",
                        Extension = ".csproj",
                        Category = FileCategory.Configuration,
                        SizeBytes = 100,
                        LineCount = 10
                    }
                ],
                Stats = new RepoStats
                {
                    TotalFiles = 10,
                    SelectedFiles = 5,
                    DetectedLanguages = ["C#"],
                    TopFolders = [new FolderStat("src", 8, 1000)]
                },
                Summary = "test",
                DiscoveryMethod = "FileSystemWalk"
            }
        };
    }
}
