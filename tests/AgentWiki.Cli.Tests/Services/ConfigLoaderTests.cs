using System.Text.Json;
using AgentWiki.Cli.Services;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentWiki.Cli.Tests.Services;

public sealed class ConfigLoaderTests
{
    [Fact]
    public async Task LoadAsync_ReturnsDefaultsWhenNoConfigFile()
    {
        var root = CreateTempDir();
        try
        {
            var sut = new ConfigLoader(NullLogger<ConfigLoader>.Instance);

            var config = await sut.LoadAsync(root);

            config.OutputPath.ShouldBe("docs/wiki");
            config.DefaultModel.ShouldNotBeNullOrWhiteSpace();
            config.RepoPath.ShouldBe(Path.GetFullPath(root));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task LoadAsync_MergesRepoConfigFile()
    {
        var root = CreateTempDir();
        try
        {
            var agentWiki = Path.Combine(root, ".agentwiki");
            Directory.CreateDirectory(agentWiki);
            var configPath = Path.Combine(agentWiki, "config.json");
            var json = JsonSerializer.Serialize(new
            {
                outputPath = "custom/wiki",
                defaultModel = "gpt-test",
                provider = "openai"
            });
            await File.WriteAllTextAsync(configPath, json);

            var sut = new ConfigLoader(NullLogger<ConfigLoader>.Instance);
            var config = await sut.LoadAsync(root);

            config.OutputPath.ShouldBe("custom/wiki");
            config.DefaultModel.ShouldBe("gpt-test");
            config.Provider.ShouldBe("openai");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ApplyCliOverrides_WinsOverLoadedConfig()
    {
        var sut = new ConfigLoader(NullLogger<ConfigLoader>.Instance);
        var baseConfig = new AgentWikiConfig
        {
            RepoPath = "/tmp/repo",
            OutputPath = "docs/wiki",
            DefaultModel = "gpt-4o",
            Provider = "azure-openai"
        };

        var merged = sut.ApplyCliOverrides(
            baseConfig,
            outputPath: "out/wiki",
            model: "gpt-override",
            provider: "openai");

        merged.OutputPath.ShouldBe("out/wiki");
        merged.DefaultModel.ShouldBe("gpt-override");
        merged.Provider.ShouldBe("openai");
        // Original unchanged
        baseConfig.OutputPath.ShouldBe("docs/wiki");
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
