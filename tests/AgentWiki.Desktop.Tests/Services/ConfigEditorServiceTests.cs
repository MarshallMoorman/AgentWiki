using AgentWiki.Core.Models;
using AgentWiki.Desktop.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentWiki.Desktop.Tests.Services;

public sealed class ConfigEditorServiceTests
{
    [Fact]
    public async Task SaveConfigJson_DoesNotPersistApiKeys()
    {
        using var temp = new TempDir();
        var svc = new ConfigEditorService(NullLogger<ConfigEditorService>.Instance);
        var config = new AgentWikiConfig
        {
            OutputPath = "docs/wiki",
            DefaultModel = "gpt-4o",
            Provider = "openai",
            OpenAI = new OpenAiOptions { ApiKey = "sk-secret", Model = "gpt-4o" },
            AzureOpenAI = new AzureOpenAiOptions { ApiKey = "az-secret", Endpoint = "https://example.openai.azure.com/" }
        };

        await svc.SaveConfigJsonAsync(temp.Path, config);

        var path = svc.GetConfigPath(temp.Path);
        File.Exists(path).ShouldBeTrue();
        var json = await File.ReadAllTextAsync(path);
        json.ShouldNotContain("sk-secret");
        json.ShouldNotContain("az-secret");
        json.ShouldContain("gpt-4o");
        json.ShouldContain("openai");
    }

    [Fact]
    public async Task SaveEnvSecrets_WritesKeys()
    {
        using var temp = new TempDir();
        var svc = new ConfigEditorService(NullLogger<ConfigEditorService>.Instance);
        await svc.SaveEnvSecretsAsync(temp.Path, openAiApiKey: "sk-test", azureApiKey: "az-test");

        var env = await File.ReadAllTextAsync(svc.GetEnvPath(temp.Path));
        env.ShouldContain("AGENTWIKI_OpenAI__ApiKey=sk-test");
        env.ShouldContain("AGENTWIKI_AzureOpenAI__ApiKey=az-test");
    }

    [Fact]
    public void MaskSecret_HidesValues()
    {
        ConfigEditorService.MaskSecret(null).ShouldBe("");
        ConfigEditorService.MaskSecret("abcd").ShouldBe("****");
        ConfigEditorService.MaskSecret("super-secret-key").ShouldContain("*");
        ConfigEditorService.MaskSecret("super-secret-key").ShouldNotContain("secret");
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "agentwiki-cfg-edit-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { /* ignore */ }
        }
    }
}
