using AgentWiki.App.Services;
using AgentWiki.Core;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentWiki.Cli.Tests.Services;

/// <summary>
/// Keeps the Phase 1–2 placeholder generator covered; production wiring uses SemanticWikiGenerator.
/// </summary>
public sealed class PlaceholderWikiGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_StillProducesInventoryBackedWiki()
    {
        var root = CreateTempDir();
        var output = Path.Combine(root, "docs", "wiki");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src"));
            await File.WriteAllTextAsync(Path.Combine(root, "src", "Program.cs"), "Console.WriteLine();\n");

            var analyzer = new RepoAnalyzer(NullLogger<RepoAnalyzer>.Instance);
            var writer = new MarkdownOutputWriter(NullLogger<MarkdownOutputWriter>.Instance);
            var sut = new PlaceholderWikiGenerator(
                analyzer,
                writer,
                NullLogger<PlaceholderWikiGenerator>.Instance);

            var result = await sut.GenerateAsync(new WikiGenerationRequest
            {
                Config = new AgentWikiConfig { Provider = Constants.Providers.Offline, OutputPath = "docs/wiki" },
                RepoPath = root,
                OutputPath = output,
                Force = true
            });

            result.Success.ShouldBeTrue(result.Error);
            result.Analysis.ShouldNotBeNull();
            File.Exists(Path.Combine(output, "inventory.md")).ShouldBeTrue();
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
