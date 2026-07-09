using AgentWiki.Cli.Services;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentWiki.Cli.Tests.Services;

public sealed class SemanticWikiGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_WritesArchitectureFromGenerator()
    {
        var root = CreateTempDir();
        var output = Path.Combine(root, "docs", "wiki");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src"));
            await File.WriteAllTextAsync(Path.Combine(root, "src", "Program.cs"), "Console.WriteLine();\n");

            var analyzer = new RepoAnalyzer(NullLogger<RepoAnalyzer>.Instance);
            var arch = new Mock<IArchitectureGenerator>();
            arch.Setup(a => a.GenerateAsync(
                    It.IsAny<RepoAnalysisResult>(),
                    It.IsAny<AgentWikiConfig>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((RepoAnalysisResult analysis, AgentWikiConfig _, string? _, string? _, CancellationToken _) =>
                    new ArchitectureDocument
                    {
                        Title = "Mock Architecture",
                        Summary = $"Architecture for {analysis.RepoName}",
                        SystemContext = "Mocked system context.",
                        Layers =
                        [
                            new ArchitectureLayer
                            {
                                Name = "src",
                                Responsibility = "Source",
                                KeyPaths = ["src/"]
                            }
                        ],
                        UsedOfflineFallback = false,
                        TokenUsage = new TokenUsage { InputTokens = 5, OutputTokens = 7 }
                    });

            var writer = new MarkdownOutputWriter(NullLogger<MarkdownOutputWriter>.Instance);
            var sut = new SemanticWikiGenerator(
                analyzer,
                arch.Object,
                writer,
                NullLogger<SemanticWikiGenerator>.Instance);

            var result = await sut.GenerateAsync(new WikiGenerationRequest
            {
                Config = new AgentWikiConfig(),
                RepoPath = root,
                OutputPath = output,
                Force = true
            });

            result.Success.ShouldBeTrue(result.Error);
            result.InputTokens.ShouldBe(5);
            result.OutputTokens.ShouldBe(7);
            File.Exists(Path.Combine(output, "architecture.md")).ShouldBeTrue();
            var architecture = await File.ReadAllTextAsync(Path.Combine(output, "architecture.md"));
            architecture.ShouldContain("Mock Architecture");
            architecture.ShouldContain("Mocked system context");
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
