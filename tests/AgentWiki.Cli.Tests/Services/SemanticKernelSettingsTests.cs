using AgentWiki.Cli.Services;
using AgentWiki.Core.Abstractions;

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
}
