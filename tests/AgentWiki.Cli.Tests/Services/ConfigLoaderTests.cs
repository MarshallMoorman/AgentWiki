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
                provider = "openai",
                llmTimeoutSeconds = 600,
                maxLlmSummaryChars = 12000
            });
            await File.WriteAllTextAsync(configPath, json);

            var sut = new ConfigLoader(NullLogger<ConfigLoader>.Instance);
            var config = await sut.LoadAsync(root);

            config.OutputPath.ShouldBe("custom/wiki");
            config.DefaultModel.ShouldBe("gpt-test");
            config.Provider.ShouldBe("openai");
            config.LlmTimeoutSeconds.ShouldBe(600);
            config.MaxLlmSummaryChars.ShouldBe(12000);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task LoadAsync_DotEnvOverridesConfigAndProcessEnv()
    {
        var root = CreateTempDir();
        var envKey = "AGENTWIKI_DefaultModel";
        var previous = Environment.GetEnvironmentVariable(envKey);
        try
        {
            Environment.SetEnvironmentVariable(envKey, "from-process");

            var agentWiki = Path.Combine(root, ".agentwiki");
            Directory.CreateDirectory(agentWiki);
            await File.WriteAllTextAsync(
                Path.Combine(agentWiki, "config.json"),
                """
                {
                  "defaultModel": "from-config",
                  "provider": "openai",
                  "llmTimeoutSeconds": 400,
                  "openAI": { "model": "from-config-openai", "apiKey": "config-key" }
                }
                """);

            await File.WriteAllTextAsync(
                Path.Combine(root, ".env"),
                """
                AGENTWIKI_DefaultModel=from-dotenv
                AGENTWIKI_LlmTimeoutSeconds=900
                AGENTWIKI_OpenAI__Model=from-dotenv-openai
                AGENTWIKI_OpenAI__ApiKey=dotenv-key
                """);

            var sut = new ConfigLoader(NullLogger<ConfigLoader>.Instance);
            var config = await sut.LoadAsync(root);

            // .env wins over config.json and process env
            config.DefaultModel.ShouldBe("from-dotenv");
            config.LlmTimeoutSeconds.ShouldBe(900);
            config.OpenAI.Model.ShouldBe("from-dotenv-openai");
            config.OpenAI.ApiKey.ShouldBe("dotenv-key");
            // config still applied where .env did not set a value
            config.Provider.ShouldBe("openai");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envKey, previous);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task LoadAsync_ConfigOverridesProcessEnv()
    {
        var root = CreateTempDir();
        var timeoutKey = "AGENTWIKI_LlmTimeoutSeconds";
        var previous = Environment.GetEnvironmentVariable(timeoutKey);
        try
        {
            Environment.SetEnvironmentVariable(timeoutKey, "111");

            var agentWiki = Path.Combine(root, ".agentwiki");
            Directory.CreateDirectory(agentWiki);
            await File.WriteAllTextAsync(
                Path.Combine(agentWiki, "config.json"),
                """{ "llmTimeoutSeconds": 600, "provider": "openai" }""");

            var sut = new ConfigLoader(NullLogger<ConfigLoader>.Instance);
            var config = await sut.LoadAsync(root);

            config.LlmTimeoutSeconds.ShouldBe(600);
        }
        finally
        {
            Environment.SetEnvironmentVariable(timeoutKey, previous);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task LoadAsync_CommentedTimeoutDoesNotOverwriteProcessEnv()
    {
        // Regression: deserializing AgentWikiConfig filled LlmTimeoutSeconds=300 (class default)
        // when the property was absent/commented, then merge stomped process env (e.g. .zshrc).
        var root = CreateTempDir();
        var timeoutKey = "AGENTWIKI_LlmTimeoutSeconds";
        var previous = Environment.GetEnvironmentVariable(timeoutKey);
        try
        {
            Environment.SetEnvironmentVariable(timeoutKey, "600");

            var agentWiki = Path.Combine(root, ".agentwiki");
            Directory.CreateDirectory(agentWiki);
            await File.WriteAllTextAsync(
                Path.Combine(agentWiki, "config.json"),
                """
                {
                  "provider": "openai",
                  "defaultModel": "gpt-chat-latest",
                  // "llmTimeoutSeconds": 300,
                  "maxFilesToAnalyze": 500
                }
                """);

            var sut = new ConfigLoader(NullLogger<ConfigLoader>.Instance);
            var config = await sut.LoadAsync(root);

            config.Provider.ShouldBe("openai");
            config.DefaultModel.ShouldBe("gpt-chat-latest");
            config.MaxFilesToAnalyze.ShouldBe(500);
            config.LlmTimeoutSeconds.ShouldBe(600);
        }
        finally
        {
            Environment.SetEnvironmentVariable(timeoutKey, previous);
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
