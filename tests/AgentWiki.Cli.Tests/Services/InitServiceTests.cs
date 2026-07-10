using AgentWiki.App.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentWiki.Cli.Tests.Services;

public sealed class InitServiceTests
{
    [Fact]
    public async Task InitializeAsync_CreatesConfigAndSamples()
    {
        var root = CreateTempDir();
        try
        {
            var sut = new InitService(NullLogger<InitService>.Instance);

            var result = await sut.InitializeAsync(root);

            result.Success.ShouldBeTrue(result.Error);
            result.FilesCreated.ShouldContain(p => p.EndsWith(".agentwiki/config.json", StringComparison.Ordinal));
            result.FilesCreated.ShouldContain(p => p.EndsWith(".env.example", StringComparison.Ordinal));

            File.Exists(Path.Combine(root, ".agentwiki", "config.json")).ShouldBeTrue();
            File.Exists(Path.Combine(root, ".agentwiki", "prompts", "SystemPrompt.txt")).ShouldBeTrue();
            File.Exists(Path.Combine(root, ".env.example")).ShouldBeTrue();

            var json = await File.ReadAllTextAsync(Path.Combine(root, ".agentwiki", "config.json"));
            json.ShouldContain("\"openAI\"");
            json.ShouldContain("\"apiKey\"");
            json.ShouldContain("\"model\"");
            // Must not be an empty openAI object — keys should be scaffolded as placeholders.
            json.ShouldNotContain("\"openAI\": {}");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotentWithoutForce()
    {
        var root = CreateTempDir();
        try
        {
            var sut = new InitService(NullLogger<InitService>.Instance);

            var first = await sut.InitializeAsync(root);
            var second = await sut.InitializeAsync(root);

            first.Success.ShouldBeTrue();
            second.Success.ShouldBeTrue();
            second.FilesCreated.Count.ShouldBe(0);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task InitializeAsync_FailsForMissingRepo()
    {
        var sut = new InitService(NullLogger<InitService>.Instance);
        var missing = Path.Combine(Path.GetTempPath(), "agentwiki-missing-" + Guid.NewGuid().ToString("N"));

        var result = await sut.InitializeAsync(missing);

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
            // best-effort cleanup
        }
    }
}
