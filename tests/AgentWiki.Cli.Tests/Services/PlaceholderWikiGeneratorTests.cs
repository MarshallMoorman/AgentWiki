using AgentWiki.Cli.Services;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentWiki.Cli.Tests.Services;

public sealed class PlaceholderWikiGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_WritesInventoryBackedWiki()
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

            var request = new WikiGenerationRequest
            {
                Config = new AgentWikiConfig { OutputPath = "docs/wiki" },
                RepoPath = root,
                OutputPath = output,
                Force = true
            };

            var result = await sut.GenerateAsync(request);

            result.Success.ShouldBeTrue(result.Error);
            result.Analysis.ShouldNotBeNull();
            result.Analysis!.Stats.TotalFiles.ShouldBeGreaterThan(0);
            result.FilesWritten.ShouldContain("inventory.md");
            File.Exists(Path.Combine(output, "index.md")).ShouldBeTrue();
            File.Exists(Path.Combine(output, "inventory.md")).ShouldBeTrue();
            File.Exists(Path.Combine(output, ".agentwiki-meta.json")).ShouldBeTrue();

            var inventory = await File.ReadAllTextAsync(Path.Combine(output, "inventory.md"));
            inventory.ShouldContain("Repository:");
            inventory.ShouldContain("Program.cs");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task GenerateAsync_DryRun_DoesNotWriteFiles()
    {
        var root = CreateTempDir();
        var output = Path.Combine(root, "docs", "wiki");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "# hi\n");

            var analyzer = new RepoAnalyzer(NullLogger<RepoAnalyzer>.Instance);
            var writer = new MarkdownOutputWriter(NullLogger<MarkdownOutputWriter>.Instance);
            var sut = new PlaceholderWikiGenerator(
                analyzer,
                writer,
                NullLogger<PlaceholderWikiGenerator>.Instance);

            var request = new WikiGenerationRequest
            {
                Config = new AgentWikiConfig(),
                RepoPath = root,
                OutputPath = output,
                DryRun = true
            };

            var result = await sut.GenerateAsync(request);

            result.Success.ShouldBeTrue(result.Error);
            result.FilesWritten.Count.ShouldBeGreaterThan(0);
            result.Analysis.ShouldNotBeNull();
            Directory.Exists(output).ShouldBeFalse();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task GenerateAsync_FailsForMissingRepo()
    {
        var missing = Path.Combine(Path.GetTempPath(), "agentwiki-missing-" + Guid.NewGuid().ToString("N"));
        var analyzer = new RepoAnalyzer(NullLogger<RepoAnalyzer>.Instance);
        var writer = new MarkdownOutputWriter(NullLogger<MarkdownOutputWriter>.Instance);
        var sut = new PlaceholderWikiGenerator(
            analyzer,
            writer,
            NullLogger<PlaceholderWikiGenerator>.Instance);

        var result = await sut.GenerateAsync(new WikiGenerationRequest
        {
            Config = new AgentWikiConfig(),
            RepoPath = missing,
            OutputPath = Path.Combine(missing, "docs", "wiki")
        });

        result.Success.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrWhiteSpace();
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
