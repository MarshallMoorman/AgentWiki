using System.Text.Json;
using AgentWiki.App.Services;
using AgentWiki.Core;
using AgentWiki.Core.Generation;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentWiki.Cli.Tests.Services;

public sealed class InitServiceTests
{
    [Fact]
    public async Task InitializeAsync_CreatesMinimalConfigExampleAndSamples()
    {
        var root = CreateTempDir();
        try
        {
            var sut = new InitService(NullLogger<InitService>.Instance);

            var result = await sut.InitializeAsync(root);

            result.Success.ShouldBeTrue(result.Error);
            result.FilesCreated.ShouldContain(p => p.EndsWith(".agentwiki/config.json", StringComparison.Ordinal));
            result.FilesCreated.ShouldContain(p =>
                p.EndsWith(".agentwiki/config.example.json", StringComparison.Ordinal));
            result.FilesCreated.ShouldContain(p => p.EndsWith(".env.example", StringComparison.Ordinal));

            File.Exists(Path.Combine(root, ".agentwiki", "config.json")).ShouldBeTrue();
            File.Exists(Path.Combine(root, ".agentwiki", "config.example.json")).ShouldBeTrue();
            File.Exists(Path.Combine(root, ".agentwiki", "prompts", "SystemPrompt.txt")).ShouldBeTrue();
            File.Exists(Path.Combine(root, ".env.example")).ShouldBeTrue();

            var minimalJson = await File.ReadAllTextAsync(Path.Combine(root, ".agentwiki", "config.json"));
            using (var doc = JsonDocument.Parse(minimalJson))
            {
                var rootEl = doc.RootElement;
                rootEl.GetProperty("provider").GetString().ShouldBe(Constants.Config.DefaultProvider);
                rootEl.GetProperty("defaultModel").GetString().ShouldBe(Constants.Config.DefaultModel);
                rootEl.GetProperty("outputPath").GetString().ShouldBe(Constants.Paths.DefaultOutputPath);

                // Bare minimum only — no nested provider blocks or API keys.
                rootEl.TryGetProperty("openAI", out _).ShouldBeFalse();
                rootEl.TryGetProperty("azureOpenAI", out _).ShouldBeFalse();
                rootEl.TryGetProperty("apiKey", out _).ShouldBeFalse();
                rootEl.EnumerateObject().Count().ShouldBe(3);
            }

            minimalJson.ShouldContain("\"openai\"");
            minimalJson.ShouldContain("\"gpt-chat-latest\"");

            var exampleJson = await File.ReadAllTextAsync(
                Path.Combine(root, ".agentwiki", "config.example.json"));
            exampleJson.ShouldContain("\"openAI\"");
            exampleJson.ShouldContain("\"azureOpenAI\"");
            exampleJson.ShouldContain("\"apiKey\"");
            exampleJson.ShouldContain("\"maxModules\"");
            exampleJson.ShouldContain("\"ignorePatterns\"");
            exampleJson.ShouldContain("\"llmTimeoutSeconds\"");

            var envExample = await File.ReadAllTextAsync(Path.Combine(root, ".env.example"));
            envExample.ShouldContain("OPENAI_API_KEY");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void CreateMinimalTemplate_HasOnlyEssentialDefaults()
    {
        var cfg = AgentWikiConfigDefaults.CreateMinimalTemplate();
        cfg.Provider.ShouldBe(Constants.Providers.OpenAi);
        cfg.DefaultModel.ShouldBe("gpt-chat-latest");
        cfg.OutputPath.ShouldBe(Constants.Paths.DefaultOutputPath);
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
