using AgentWiki.Cli.Services;
using AgentWiki.Core.Abstractions;
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
                NullLogger<WikiGenerationOrchestrator>.Instance);

            var writer = new MarkdownOutputWriter(NullLogger<MarkdownOutputWriter>.Instance);
            var bootstrapper = new AgentBootstrapper(NullLogger<AgentBootstrapper>.Instance);
            var sut = new SemanticWikiGenerator(
                analyzer,
                orchestrator,
                writer,
                bootstrapper,
                NullLogger<SemanticWikiGenerator>.Instance);

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
            Directory.Exists(Path.Combine(output, "cross-cutting")).ShouldBeTrue();
            File.Exists(Path.Combine(root, "AGENTS.md")).ShouldBeTrue();

            var agents = await File.ReadAllTextAsync(Path.Combine(root, "AGENTS.md"));
            agents.ShouldContain("docs/wiki/index.md");
        }
        finally
        {
            TryDelete(root);
        }
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
