using AgentWiki.App.Services;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentWiki.Cli.Tests.Services;

public sealed class WikiGenerationOrchestratorTests
{
    [Fact]
    public async Task GenerateAsync_OfflinePipeline_ProducesModulesAndCrossCutting()
    {
        var analysis = CreateAnalysis();
        var arch = new Mock<IArchitectureGenerator>();
        arch.Setup(a => a.GenerateAsync(
                It.IsAny<RepoAnalysisResult>(),
                It.IsAny<AgentWikiConfig>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OfflineArchitectureGenerator.Generate(analysis));

        var llm = new Mock<ILlmCompletionService>();
        llm.Setup(x => x.CanUseLiveLlm(It.IsAny<AgentWikiConfig>(), It.IsAny<string?>()))
            .Returns(false);

        var prompts = new PromptManager(NullLogger<PromptManager>.Instance);
        var sut = new WikiGenerationOrchestrator(
            arch.Object,
            llm.Object,
            prompts,
            NullLogger<WikiGenerationOrchestrator>.Instance);

        var bundle = await sut.GenerateAsync(
            analysis,
            new WikiGenerationRequest
            {
                Config = new AgentWikiConfig(),
                RepoPath = analysis.RepoPath,
                OutputPath = Path.Combine(analysis.RepoPath, "docs", "wiki")
            },
            scope: IncrementalScope.Full());

        bundle.Modules.Count.ShouldBeGreaterThan(0);
        bundle.CrossCutting.Count.ShouldBeGreaterThan(0);
        bundle.Sections.Any(s => s.RelativePath == "index.md").ShouldBeTrue();
        bundle.Sections.Any(s => s.RelativePath == "architecture.md").ShouldBeTrue();
        bundle.Sections.Any(s => s.RelativePath.StartsWith("modules/", StringComparison.Ordinal)).ShouldBeTrue();
        bundle.Sections.Any(s => s.RelativePath.StartsWith("cross-cutting/", StringComparison.Ordinal)).ShouldBeTrue();
        bundle.StepsCompleted.Count.ShouldBeGreaterThan(3);
        bundle.UsedOfflineFallback.ShouldBeTrue();
    }

    [Fact]
    public void ParseModulePlan_AcceptsObjectAndArray()
    {
        var plan = WikiGenerationOrchestrator.ParseModulePlan(
            """
            { "modules": [ { "id": "cli", "name": "CLI", "summary": "x", "rootPaths": ["src/Cli/"], "relatedFiles": [] } ] }
            """);
        plan.Modules.Count.ShouldBe(1);
        plan.Modules[0].Id.ShouldBe("cli");

        var plan2 = WikiGenerationOrchestrator.ParseModulePlan(
            """
            [ { "id": "core", "name": "Core", "summary": "y", "rootPaths": ["src/Core/"], "relatedFiles": [] } ]
            """);
        plan2.Modules[0].Id.ShouldBe("core");
    }

    private static RepoAnalysisResult CreateAnalysis()
    {
        var files = new List<RepoFile>
        {
            new()
            {
                RelativePath = "src/App/App.csproj",
                AbsolutePath = "/tmp/src/App/App.csproj",
                Category = FileCategory.Configuration,
                SizeBytes = 50,
                Extension = ".csproj",
                SelectedForAnalysis = true
            },
            new()
            {
                RelativePath = "src/App/Program.cs",
                AbsolutePath = "/tmp/src/App/Program.cs",
                Category = FileCategory.SourceCode,
                SizeBytes = 100,
                Extension = ".cs",
                Language = "C#",
                LineCount = 20,
                SelectedForAnalysis = true
            },
            new()
            {
                RelativePath = "src/App/Services/Worker.cs",
                AbsolutePath = "/tmp/src/App/Services/Worker.cs",
                Category = FileCategory.SourceCode,
                SizeBytes = 80,
                Extension = ".cs",
                Language = "C#",
                LineCount = 15,
                SelectedForAnalysis = true
            },
            new()
            {
                RelativePath = "tests/App.Tests/UnitTests.cs",
                AbsolutePath = "/tmp/tests/App.Tests/UnitTests.cs",
                Category = FileCategory.Tests,
                SizeBytes = 40,
                Extension = ".cs",
                Language = "C#",
                LineCount = 10,
                SelectedForAnalysis = true
            },
            new()
            {
                RelativePath = "appsettings.json",
                AbsolutePath = "/tmp/appsettings.json",
                Category = FileCategory.Configuration,
                SizeBytes = 30,
                Extension = ".json",
                Language = "JSON",
                SelectedForAnalysis = true
            }
        };

        var stats = new RepoStats
        {
            TotalFiles = files.Count,
            SelectedFiles = files.Count,
            TotalSizeBytes = files.Sum(f => f.SizeBytes),
            TotalLines = files.Sum(f => f.LineCount ?? 0),
            FilesByCategory = files.GroupBy(f => f.Category).ToDictionary(g => g.Key, g => g.Count()),
            FilesByExtension = files.GroupBy(f => f.Extension ?? "").ToDictionary(g => g.Key, g => g.Count()),
            FilesByLanguage = new Dictionary<string, int> { ["C#"] = 3, ["JSON"] = 1 },
            TopFolders =
            [
                new FolderStat("src", 3, 230),
                new FolderStat("tests", 1, 40)
            ],
            DetectedLanguages = ["C#", "JSON"]
        };

        return new RepoAnalysisResult
        {
            RepoPath = "/tmp/repo",
            RepoName = "repo",
            Files = files,
            Stats = stats,
            Summary = RepoSummaryBuilder.Build("repo", "/tmp/repo", stats, files),
            DiscoveryMethod = "FileSystemWalk"
        };
    }
}
