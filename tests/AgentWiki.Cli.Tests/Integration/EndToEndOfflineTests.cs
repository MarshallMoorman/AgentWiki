using AgentWiki.App.Infrastructure;
using AgentWiki.App.Services;
using AgentWiki.Core;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentWiki.Cli.Tests.Integration;

/// <summary>
/// Offline end-to-end coverage of init → generate → update(no-op path via mocked detector).
/// </summary>
public sealed class EndToEndOfflineTests
{
    [Fact]
    public async Task Init_Generate_ProducesWikiAndAgentsMd()
    {
        var root = CreateTempDir();
        try
        {
            // Arrange a tiny repo
            Directory.CreateDirectory(Path.Combine(root, "src", "Demo"));
            await File.WriteAllTextAsync(Path.Combine(root, "src", "Demo", "Demo.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
            await File.WriteAllTextAsync(Path.Combine(root, "src", "Demo", "Program.cs"), "Console.WriteLine(\"hi\");\n");
            await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "# Demo\n");

            var init = new InitService(NullLogger<InitService>.Instance);
            var initResult = await init.InitializeAsync(root);
            initResult.Success.ShouldBeTrue(initResult.Error);
            File.Exists(Path.Combine(root, ".agentwiki", "config.json")).ShouldBeTrue();

            var generator = CreateGenerator();
            var output = Path.Combine(root, "docs", "wiki");
            var result = await generator.GenerateAsync(new WikiGenerationRequest
            {
                Config = new AgentWikiConfig { Provider = Constants.Providers.Offline, OutputPath = "docs/wiki", AgentMdPath = "AGENTS.md" },
                RepoPath = root,
                OutputPath = output,
                Force = true
            });

            result.Success.ShouldBeTrue(result.Error);
            result.FilesWritten.Count.ShouldBeGreaterThan(5);
            File.Exists(Path.Combine(output, "index.md")).ShouldBeTrue();
            File.Exists(Path.Combine(output, "architecture.md")).ShouldBeTrue();
            Directory.Exists(Path.Combine(output, "modules")).ShouldBeTrue();
            Directory.Exists(Path.Combine(output, "cross-cutting")).ShouldBeTrue();
            File.Exists(Path.Combine(root, "AGENTS.md")).ShouldBeTrue();
            File.Exists(Path.Combine(root, ".agentwiki", "last-run.json")).ShouldBeTrue();

            var agents = await File.ReadAllTextAsync(Path.Combine(root, "AGENTS.md"));
            agents.ShouldContain("docs/wiki/index.md");
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static SemanticWikiGenerator CreateGenerator()
    {
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

        var changeDetector = new Mock<IChangeDetector>();
        changeDetector.Setup(c => c.DetectAsync(
                It.IsAny<string>(),
                It.IsAny<AgentWikiConfig>(),
                It.IsAny<RepoAnalysisResult?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ChangeDetectionResult.Full("test"));

        var analyzer = new RepoAnalyzer(NullLogger<RepoAnalyzer>.Instance);
        var staticAnalyzer = new RoslynStaticAnalyzer(NullLogger<RoslynStaticAnalyzer>.Instance);
        var agentsMd = new AgentsMdGenerator(
            analyzer,
            staticAnalyzer,
            llm.Object,
            NullLogger<AgentsMdGenerator>.Instance);
        var readme = new ReadmeGenerator(analyzer, llm.Object, NullLogger<ReadmeGenerator>.Instance);

        return new SemanticWikiGenerator(
            analyzer,
            staticAnalyzer,
            new WikiGenerationOrchestrator(
                arch.Object,
                llm.Object,
                new PromptManager(NullLogger<PromptManager>.Instance),
                new WikiPostProcessor(),
                NullLogger<WikiGenerationOrchestrator>.Instance),
            new MarkdownOutputWriter(NullLogger<MarkdownOutputWriter>.Instance),
            new AgentBootstrapper(NullLogger<AgentBootstrapper>.Instance),
            agentsMd,
            readme,
            changeDetector.Object,
            new LastRunStore(NullLogger<LastRunStore>.Instance),
            new NullRunTelemetry(),
            NullLogger<SemanticWikiGenerator>.Instance);
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentwiki-e2e-" + Guid.NewGuid().ToString("N"));
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
