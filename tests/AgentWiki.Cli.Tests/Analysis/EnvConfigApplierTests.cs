using AgentWiki.Core.Analysis;
using AgentWiki.Core.Models;

namespace AgentWiki.Cli.Tests.Analysis;

public sealed class EnvConfigApplierTests
{
    [Fact]
    public void Apply_SetsRootAndNestedLlmSettings()
    {
        var config = new AgentWikiConfig();
        EnvConfigApplier.Apply(config,
        [
            new("AGENTWIKI_Provider", "openai"),
            new("AGENTWIKI_DefaultModel", "gpt-test"),
            new("AGENTWIKI_LlmTimeoutSeconds", "600"),
            new("AGENTWIKI_MaxLlmSummaryChars", "8000"),
            new("AGENTWIKI_AzureOpenAI__Endpoint", "https://azure.example/"),
            new("AGENTWIKI_AzureOpenAI__DeploymentName", "dep-1"),
            new("AGENTWIKI_AzureOpenAI__ApiKey", "azure-key"),
            new("AGENTWIKI_OpenAI__Endpoint", "https://openai.example/v1"),
            new("AGENTWIKI_OpenAI__ApiKey", "openai-key"),
            new("AGENTWIKI_OpenAI__Model", "gpt-chat-latest"),
            new("AGENTWIKI_EnablePostProcessing", "false"),
            new("AGENTWIKI_PostProcessingMode", "strict")
        ]);

        config.Provider.ShouldBe("openai");
        config.DefaultModel.ShouldBe("gpt-test");
        config.LlmTimeoutSeconds.ShouldBe(600);
        config.MaxLlmSummaryChars.ShouldBe(8000);
        config.EnablePostProcessing.ShouldBeFalse();
        config.PostProcessingMode.ShouldBe("strict");
        config.AzureOpenAI.Endpoint.ShouldBe("https://azure.example/");
        config.AzureOpenAI.DeploymentName.ShouldBe("dep-1");
        config.AzureOpenAI.ApiKey.ShouldBe("azure-key");
        config.OpenAI.Endpoint.ShouldBe("https://openai.example/v1");
        config.OpenAI.ApiKey.ShouldBe("openai-key");
        config.OpenAI.Model.ShouldBe("gpt-chat-latest");
    }
}
