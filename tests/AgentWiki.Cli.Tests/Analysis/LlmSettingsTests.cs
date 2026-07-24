using AgentWiki.Core;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Models;

namespace AgentWiki.Cli.Tests.Analysis;

public sealed class LlmSettingsTests
{
    [Theory]
    [InlineData("offline", true)]
    [InlineData("none", true)]
    [InlineData("mock", true)]
    [InlineData("openai", false)]
    [InlineData("azure-openai", false)]
    [InlineData("github-models", false)]
    public void IsExplicitOfflineMode_MatchesProvider(string provider, bool expected) =>
        LlmSettings.IsExplicitOfflineMode(provider).ShouldBe(expected);

    [Fact]
    public void EnsureLiveLlmConfigured_AllowsOfflineProviderWithoutCredentials()
    {
        var config = new AgentWikiConfig { Provider = Constants.Providers.Offline };
        Should.NotThrow(() =>
            LlmSettings.EnsureLiveLlmConfigured(config, providerOverride: null, canUseLiveLlm: false));
    }

    [Fact]
    public void EnsureLiveLlmConfigured_ThrowsForLiveProviderWithoutCredentials()
    {
        var config = new AgentWikiConfig
        {
            Provider = Constants.Providers.OpenAi,
            OpenAI = new OpenAiOptions { ApiKey = "" }
        };

        var ex = Should.Throw<InvalidOperationException>(() =>
            LlmSettings.EnsureLiveLlmConfigured(config, providerOverride: null, canUseLiveLlm: false));
        ex.Message.ShouldContain("LLM is required");
        ex.Message.ShouldContain("offline");
    }


    [Fact]
    public void ResolveModel_UsesDefaultModelWhenProviderModelEmpty()
    {
        var config = new AgentWikiConfig
        {
            Provider = "openai",
            DefaultModel = "gpt-chat-latest",
            OpenAI = new OpenAiOptions { Model = "" }
        };

        LlmSettings.ResolveModel(config).ShouldBe("gpt-chat-latest");
    }

    [Fact]
    public void ResolveModel_PrefersNonEmptyProviderModel()
    {
        var config = new AgentWikiConfig
        {
            Provider = "openai",
            DefaultModel = "gpt-chat-latest",
            OpenAI = new OpenAiOptions { Model = "gpt-4o" }
        };

        LlmSettings.ResolveModel(config).ShouldBe("gpt-4o");
    }

    [Fact]
    public void ResolveModel_CliOverrideWins()
    {
        var config = new AgentWikiConfig
        {
            Provider = "openai",
            DefaultModel = "gpt-chat-latest",
            OpenAI = new OpenAiOptions { Model = "gpt-4o" }
        };

        LlmSettings.ResolveModel(config, modelOverride: "override-model").ShouldBe("override-model");
    }

    [Fact]
    public void DescribeNotReadyReason_ReportsMissingOpenAiKey()
    {
        var config = new AgentWikiConfig
        {
            Provider = "openai",
            OpenAI = new OpenAiOptions { ApiKey = "" }
        };

        LlmSettings.DescribeNotReadyReason(config).ShouldNotBeNull();
        LlmSettings.DescribeNotReadyReason(config)!.ShouldContain("API key");
    }
}
