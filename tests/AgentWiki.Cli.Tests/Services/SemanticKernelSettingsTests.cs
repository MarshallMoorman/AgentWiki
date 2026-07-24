using AgentWiki.App.Services;
using AgentWiki.Core.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AgentWiki.Cli.Tests.Services;

public sealed class SemanticKernelSettingsTests
{
    [Theory]
    [InlineData("o1", false)]
    [InlineData("o1-mini", false)]
    [InlineData("o3-mini", false)]
    [InlineData("gpt-5", false)]
    [InlineData("gpt-4o", true)]
    [InlineData("gpt-4.1", true)]
    public void SupportsTemperature_MatchesKnownFamilies(string model, bool expected)
    {
        SemanticKernelLlmCompletionService.SupportsTemperature(model).ShouldBe(expected);
    }

    [Fact]
    public void CreateExecutionSettings_OmitsTemperatureByDefault()
    {
        var settings = SemanticKernelLlmCompletionService.CreateExecutionSettings(
            "gpt-4o",
            LlmRequestOptions.WikiGeneration);

        settings.Temperature.ShouldBeNull();
        settings.ResponseFormat.ShouldNotBeNull();
    }

    [Fact]
    public void CreateExecutionSettings_Probe_DoesNotRequireJson()
    {
        var settings = SemanticKernelLlmCompletionService.CreateExecutionSettings(
            "gpt-4o",
            LlmRequestOptions.ConnectivityProbe);

        settings.ResponseFormat.ShouldBeNull();
        settings.Temperature.ShouldBeNull();
    }

    [Fact]
    public void EnsureJsonMentionInMessages_AppendsWhenMissing()
    {
        var (system, user) = SemanticKernelLlmCompletionService.EnsureJsonMentionInMessages(
            "You are a helper.",
            "Describe the module.");

        (system + user).ShouldContain("json", Case.Insensitive);
        user.ShouldContain("JSON");
    }

    [Fact]
    public void EnsureJsonMentionInMessages_LeavesExistingJsonAlone()
    {
        var originalUser = "Return a JSON object with fields.";
        var (system, user) = SemanticKernelLlmCompletionService.EnsureJsonMentionInMessages(
            "System",
            originalUser);

        user.ShouldBe(originalUser);
        system.ShouldBe("System");
    }

    [Fact]
    public void TryReadUsage_ReadsFlatMetadataKeys()
    {
        var message = new ChatMessageContent(AuthorRole.Assistant, "ok")
        {
            Metadata = new Dictionary<string, object?>
            {
                ["PromptTokenCount"] = 1200,
                ["CompletionTokenCount"] = 340
            }
        };

        var usage = SemanticKernelLlmCompletionService.TryReadUsage(message);
        usage.ShouldNotBeNull();
        usage!.InputTokens.ShouldBe(1200);
        usage.OutputTokens.ShouldBe(340);
    }

    [Fact]
    public void TryReadUsage_ReadsUsageObjectWithOpenAiPropertyNames()
    {
        var message = new ChatMessageContent(AuthorRole.Assistant, "ok")
        {
            Metadata = new Dictionary<string, object?>
            {
                ["Usage"] = new FakeChatTokenUsage(InputTokenCount: 50, OutputTokenCount: 25)
            }
        };

        var usage = SemanticKernelLlmCompletionService.TryReadUsage(message);
        usage.ShouldNotBeNull();
        usage!.InputTokens.ShouldBe(50);
        usage.OutputTokens.ShouldBe(25);
    }

    [Fact]
    public void TryReadUsage_ReadsUsageObjectWithLegacyCompletionsNames()
    {
        var message = new ChatMessageContent(AuthorRole.Assistant, "ok")
        {
            Metadata = new Dictionary<string, object?>
            {
                ["Usage"] = new FakeCompletionsUsage(PromptTokens: 11, CompletionTokens: 7)
            }
        };

        var usage = SemanticKernelLlmCompletionService.TryReadUsage(message);
        usage.ShouldNotBeNull();
        usage!.InputTokens.ShouldBe(11);
        usage.OutputTokens.ShouldBe(7);
    }

    [Fact]
    public void TryReadUsage_ReturnsNullWhenNoUsagePresent()
    {
        var message = new ChatMessageContent(AuthorRole.Assistant, "ok");
        SemanticKernelLlmCompletionService.TryReadUsage(message).ShouldBeNull();
    }

    private sealed class FakeChatTokenUsage(int InputTokenCount, int OutputTokenCount)
    {
        public int InputTokenCount { get; } = InputTokenCount;
        public int OutputTokenCount { get; } = OutputTokenCount;
    }

    private sealed class FakeCompletionsUsage(int PromptTokens, int CompletionTokens)
    {
        public int PromptTokens { get; } = PromptTokens;
        public int CompletionTokens { get; } = CompletionTokens;
    }
}
