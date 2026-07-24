using AgentWiki.App.Services;
using AgentWiki.Core;
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
            new WikiPostProcessor(),
            NullLogger<WikiGenerationOrchestrator>.Instance);

        var bundle = await sut.GenerateAsync(
            analysis,
            new WikiGenerationRequest
            {
                Config = new AgentWikiConfig { Provider = Constants.Providers.Offline },
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
        bundle.StepsCompleted.ShouldContain("post-process-structured");
        bundle.StepsCompleted.ShouldContain("post-process-markdown");
        bundle.Sections.Any(s => s.RelativePath == "api-endpoints.md").ShouldBeTrue();
    }

    [Fact]
    public async Task GenerateAsync_WithStaticEndpoints_EmitsApiEndpointsPageAndModuleSections()
    {
        var analysis = CreateAnalysis();
        analysis.StaticAnalysis = new StaticAnalysisResult
        {
            Enabled = true,
            Succeeded = true,
            UsedRoslyn = true,
            Summary = "test endpoints",
            Endpoints =
            [
                new EndpointInfo
                {
                    HttpMethod = "GET",
                    Route = "/api/app",
                    HandlerName = "AppController.Get",
                    Kind = "controller",
                    RelativePath = "src/App/AppController.cs",
                    ProjectName = "App",
                    AuthHints = ["Authorize"],
                    Parameters = ["int id"]
                },
                new EndpointInfo
                {
                    HttpMethod = "GET",
                    Route = "/health",
                    HandlerName = "Program.MapGet",
                    Kind = "minimal-api",
                    RelativePath = "src/App/Program.cs",
                    ProjectName = "App"
                }
            ]
        };

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

        var sut = new WikiGenerationOrchestrator(
            arch.Object,
            llm.Object,
            new PromptManager(NullLogger<PromptManager>.Instance),
            new WikiPostProcessor(),
            NullLogger<WikiGenerationOrchestrator>.Instance);

        var bundle = await sut.GenerateAsync(
            analysis,
            new WikiGenerationRequest
            {
                Config = new AgentWikiConfig { Provider = Constants.Providers.Offline, EnableApiEndpointDocs = true },
                RepoPath = analysis.RepoPath,
                OutputPath = Path.Combine(analysis.RepoPath, "docs", "wiki")
            },
            scope: IncrementalScope.Full());

        var apiPage = bundle.Sections.Single(s => s.RelativePath == "api-endpoints.md");
        apiPage.Content.ShouldContain("/api/app");
        apiPage.Content.ShouldContain("/health");
        apiPage.Content.ShouldContain("Authorize");

        var keyComponents = bundle.Sections.Single(s => s.RelativePath == "key-components.md");
        keyComponents.Content.ShouldContain("Public API endpoints");
        keyComponents.Content.ShouldContain("api-endpoints.md");

        bundle.Modules.ShouldContain(m => m.Endpoints.Count > 0);
        var moduleWithEndpoints = bundle.Modules.First(m => m.Endpoints.Count > 0);
        var moduleSection = bundle.Sections.Single(s => s.RelativePath == moduleWithEndpoints.RelativePath);
        moduleSection.Content.ShouldContain("Endpoints / Public API");

        bundle.StepsCompleted.ShouldContain(s => s.StartsWith("api-endpoints:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GenerateAsync_PostProcessor_CleansInjectedAbsolutePaths()
    {
        var analysis = CreateAnalysis();
        var absPath = Path.Combine(analysis.RepoPath, "src", "App", "Program.cs");
        var arch = new Mock<IArchitectureGenerator>();
        arch.Setup(a => a.GenerateAsync(
                It.IsAny<RepoAnalysisResult>(),
                It.IsAny<AgentWikiConfig>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ArchitectureDocument
            {
                Title = "Architecture",
                Summary = $"Entry at {absPath}. This component is deprecated.",
                UsedOfflineFallback = true,
                KeyComponents =
                [
                    new ArchitectureComponent
                    {
                        Name = "App",
                        Path = absPath,
                        Purpose = "Host"
                    }
                ]
            });

        var llm = new Mock<ILlmCompletionService>();
        llm.Setup(x => x.CanUseLiveLlm(It.IsAny<AgentWikiConfig>(), It.IsAny<string?>()))
            .Returns(false);

        var sut = new WikiGenerationOrchestrator(
            arch.Object,
            llm.Object,
            new PromptManager(NullLogger<PromptManager>.Instance),
            new WikiPostProcessor(),
            NullLogger<WikiGenerationOrchestrator>.Instance);

        var bundle = await sut.GenerateAsync(
            analysis,
            new WikiGenerationRequest
            {
                Config = new AgentWikiConfig { Provider = Constants.Providers.Offline, EnablePostProcessing = true },
                RepoPath = analysis.RepoPath,
                OutputPath = Path.Combine(analysis.RepoPath, "docs", "wiki")
            },
            scope: IncrementalScope.Full());

        bundle.Architecture.Summary.ShouldNotContain(analysis.RepoPath);
        bundle.Architecture.KeyComponents[0].Path.ShouldBe("src/App/Program.cs");
        bundle.Architecture.Summary.ShouldNotContain("deprecated", Case.Insensitive);
        bundle.Warnings.ShouldContain(w => w.Contains("Post-processor", StringComparison.OrdinalIgnoreCase));

        var archSection = bundle.Sections.Single(s => s.RelativePath == "architecture.md");
        archSection.Content.ShouldNotContain("/tmp/repo");
    }

    [Fact]
    public async Task GenerateAsync_PostProcessorDisabled_IsSkipped()
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

        var sut = new WikiGenerationOrchestrator(
            arch.Object,
            llm.Object,
            new PromptManager(NullLogger<PromptManager>.Instance),
            new WikiPostProcessor(),
            NullLogger<WikiGenerationOrchestrator>.Instance);

        var bundle = await sut.GenerateAsync(
            analysis,
            new WikiGenerationRequest
            {
                Config = new AgentWikiConfig { Provider = Constants.Providers.Offline, EnablePostProcessing = false },
                RepoPath = analysis.RepoPath,
                OutputPath = Path.Combine(analysis.RepoPath, "docs", "wiki")
            },
            scope: IncrementalScope.Full());

        bundle.StepsCompleted.ShouldNotContain("post-process-structured");
        bundle.StepsCompleted.ShouldNotContain("post-process-markdown");
        bundle.Warnings.ShouldNotContain(w => w.Contains("Post-processor", StringComparison.OrdinalIgnoreCase));
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
