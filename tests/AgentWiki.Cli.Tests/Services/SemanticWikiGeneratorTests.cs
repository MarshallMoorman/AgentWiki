using AgentWiki.App.Infrastructure;
using AgentWiki.App.Services;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentWiki.Cli.Tests.Services;

public sealed class SemanticWikiGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_WritesModulesAndBootstrapsAgentsMd()
    {
        var root = CreateTempDir();
        var output = Path.Combine(root, "docs", "wiki");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src", "App"));
            await File.WriteAllTextAsync(Path.Combine(root, "src", "App", "App.csproj"), "<Project />\n");
            await File.WriteAllTextAsync(Path.Combine(root, "src", "App", "Program.cs"), "Console.WriteLine();\n");

            var sut = CreateGenerator(root, fullChanges: true);

            var result = await sut.GenerateAsync(new WikiGenerationRequest
            {
                Config = new AgentWikiConfig { OutputPath = "docs/wiki", AgentMdPath = "AGENTS.md" },
                RepoPath = root,
                OutputPath = output,
                Force = true
            });

            result.Success.ShouldBeTrue(result.Error);
            File.Exists(Path.Combine(output, "index.md")).ShouldBeTrue();
            File.Exists(Path.Combine(output, "architecture.md")).ShouldBeTrue();
            Directory.Exists(Path.Combine(output, "modules")).ShouldBeTrue();
            File.Exists(Path.Combine(root, "AGENTS.md")).ShouldBeTrue();
            File.Exists(Path.Combine(root, ".agentwiki", "last-run.json")).ShouldBeTrue();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task GenerateAsync_IncrementalNoChanges_WritesNothing()
    {
        var root = CreateTempDir();
        var output = Path.Combine(root, "docs", "wiki");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src"));
            await File.WriteAllTextAsync(Path.Combine(root, "src", "Program.cs"), "class A {}");

            var changeDetector = new Mock<IChangeDetector>();
            changeDetector
                .Setup(c => c.DetectAsync(
                    It.IsAny<string>(),
                    It.IsAny<AgentWikiConfig>(),
                    It.IsAny<RepoAnalysisResult?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChangeDetectionResult
                {
                    HasBaseline = true,
                    RequiresFullRegeneration = false,
                    NoChanges = true,
                    BaselineCommitSha = "abc1234",
                    CurrentCommitSha = "abc1234",
                    Reason = "No changes since last run.",
                    DetectionMethod = "git"
                });

            var sut = CreateGenerator(root, changeDetector: changeDetector.Object);

            var result = await sut.GenerateAsync(new WikiGenerationRequest
            {
                Config = new AgentWikiConfig(),
                RepoPath = root,
                OutputPath = output,
                Incremental = true,
                Force = true
            });

            result.Success.ShouldBeTrue(result.Error);
            result.FilesWritten.Count.ShouldBe(0);
            result.ChangeDetection.ShouldNotBeNull();
            result.ChangeDetection!.NoChanges.ShouldBeTrue();
            Directory.Exists(output).ShouldBeFalse();
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static SemanticWikiGenerator CreateGenerator(
        string root,
        bool fullChanges = false,
        IChangeDetector? changeDetector = null)
    {
        var analyzer = new RepoAnalyzer(NullLogger<RepoAnalyzer>.Instance);
        var arch = new Mock<IArchitectureGenerator>();
        arch.Setup(a => a.GenerateAsync(
                It.IsAny<RepoAnalysisResult>(),
                It.IsAny<AgentWikiConfig>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((RepoAnalysisResult a, AgentWikiConfig _, string? _, string? _, CancellationToken _) =>
                OfflineArchitectureGenerator.Generate(a));

        var llm = new Mock<ILlmCompletionService>();
        llm.Setup(x => x.CanUseLiveLlm(It.IsAny<AgentWikiConfig>(), It.IsAny<string?>())).Returns(false);

        var orchestrator = new WikiGenerationOrchestrator(
            arch.Object,
            llm.Object,
            new PromptManager(NullLogger<PromptManager>.Instance),
            new WikiPostProcessor(),
            NullLogger<WikiGenerationOrchestrator>.Instance);

        if (changeDetector is null)
        {
            var mock = new Mock<IChangeDetector>();
            mock.Setup(c => c.DetectAsync(
                    It.IsAny<string>(),
                    It.IsAny<AgentWikiConfig>(),
                    It.IsAny<RepoAnalysisResult?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ChangeDetectionResult.Full("test full"));
            changeDetector = mock.Object;
        }

        var agentsMd = new AgentsMdGenerator(
            analyzer,
            new RoslynStaticAnalyzer(NullLogger<RoslynStaticAnalyzer>.Instance),
            llm.Object,
            NullLogger<AgentsMdGenerator>.Instance);
        var readme = new ReadmeGenerator(analyzer, NullLogger<ReadmeGenerator>.Instance);

        return new SemanticWikiGenerator(
            analyzer,
            new RoslynStaticAnalyzer(NullLogger<RoslynStaticAnalyzer>.Instance),
            orchestrator,
            new MarkdownOutputWriter(NullLogger<MarkdownOutputWriter>.Instance),
            new AgentBootstrapper(NullLogger<AgentBootstrapper>.Instance),
            agentsMd,
            readme,
            changeDetector,
            new LastRunStore(NullLogger<LastRunStore>.Instance),
            new NullRunTelemetry(),
            NullLogger<SemanticWikiGenerator>.Instance);
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentwiki-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // best-effort
        }
    }
}
