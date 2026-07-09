using AgentWiki.Cli.Services;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentWiki.Cli.Tests.Services;

public sealed class RepoAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_BuildsInventoryAndRespectsGitignore()
    {
        var root = CreateTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src", "App"));
            Directory.CreateDirectory(Path.Combine(root, "src", "App", "bin", "Debug"));
            Directory.CreateDirectory(Path.Combine(root, "tests", "App.Tests"));
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            Directory.CreateDirectory(Path.Combine(root, "node_modules", "left-pad"));

            await File.WriteAllTextAsync(Path.Combine(root, ".gitignore"), "secret.txt\n");
            await File.WriteAllTextAsync(Path.Combine(root, "src", "App", "Program.cs"), "Console.WriteLine();\n");
            await File.WriteAllTextAsync(Path.Combine(root, "src", "App", "App.csproj"), "<Project />\n");
            await File.WriteAllTextAsync(Path.Combine(root, "src", "App", "bin", "Debug", "App.dll"), "bin");
            await File.WriteAllTextAsync(Path.Combine(root, "tests", "App.Tests", "UnitTests.cs"), "public class UnitTests {}");
            await File.WriteAllTextAsync(Path.Combine(root, "docs", "readme.md"), "# Docs\n");
            await File.WriteAllTextAsync(Path.Combine(root, "secret.txt"), "do-not-read");
            await File.WriteAllTextAsync(Path.Combine(root, "node_modules", "left-pad", "index.js"), "module.exports=1");

            var sut = new RepoAnalyzer(NullLogger<RepoAnalyzer>.Instance);
            var config = new AgentWikiConfig
            {
                MaxFilesToAnalyze = 50,
                IgnorePatterns =
                [
                    "**/bin/**",
                    "**/obj/**",
                    "**/node_modules/**"
                ]
            };

            var result = await sut.AnalyzeAsync(root, config);

            result.Stats.TotalFiles.ShouldBeGreaterThan(0);
            result.Files.Any(f => f.RelativePath.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase)).ShouldBeTrue();
            result.Files.Any(f => f.RelativePath.Contains("bin/", StringComparison.OrdinalIgnoreCase)).ShouldBeFalse();
            result.Files.Any(f => f.RelativePath.Contains("node_modules", StringComparison.OrdinalIgnoreCase)).ShouldBeFalse();
            result.Files.Any(f => f.RelativePath.Equals("secret.txt", StringComparison.OrdinalIgnoreCase)).ShouldBeFalse();

            result.Stats.FilesByCategory[FileCategory.SourceCode].ShouldBeGreaterThan(0);
            result.Stats.FilesByCategory[FileCategory.Tests].ShouldBeGreaterThan(0);
            result.Stats.FilesByCategory[FileCategory.Configuration].ShouldBeGreaterThan(0);
            result.Stats.DetectedLanguages.ShouldContain("C#");
            result.Summary.ShouldContain("Repository:");
            result.Stats.SelectedFiles.ShouldBeLessThanOrEqualTo(config.MaxFilesToAnalyze);
            result.DiscoveryMethod.ShouldBeOneOf("Git", "FileSystemWalk");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task AnalyzeAsync_HonorsMaxFilesToAnalyzeSelection()
    {
        var root = CreateTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src"));
            for (var i = 0; i < 10; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(root, "src", $"File{i}.cs"), $"// file {i}\n");
            }

            var sut = new RepoAnalyzer(NullLogger<RepoAnalyzer>.Instance);
            var config = new AgentWikiConfig { MaxFilesToAnalyze = 3 };

            var result = await sut.AnalyzeAsync(root, config);

            result.Stats.TotalFiles.ShouldBe(10);
            result.Stats.SelectedFiles.ShouldBe(3);
            result.Files.Count(f => f.SelectedForAnalysis).ShouldBe(3);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task AnalyzeAsync_ThrowsForMissingPath()
    {
        var sut = new RepoAnalyzer(NullLogger<RepoAnalyzer>.Instance);
        var missing = Path.Combine(Path.GetTempPath(), "agentwiki-missing-" + Guid.NewGuid().ToString("N"));

        await Should.ThrowAsync<DirectoryNotFoundException>(async () =>
            await sut.AnalyzeAsync(missing, new AgentWikiConfig()));
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
