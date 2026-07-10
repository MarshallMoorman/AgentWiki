using AgentWiki.Core.Analysis;
using AgentWiki.Core.Models;

namespace AgentWiki.Cli.Tests.Analysis;

public sealed class LlmSettingsTests
{
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
