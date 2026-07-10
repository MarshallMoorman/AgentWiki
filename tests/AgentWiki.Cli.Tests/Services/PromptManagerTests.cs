using AgentWiki.App.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentWiki.Cli.Tests.Services;

public sealed class PromptManagerTests
{
    [Fact]
    public void GetPrompt_LoadsEmbeddedSystemPrompt()
    {
        var sut = new PromptManager(NullLogger<PromptManager>.Instance);

        var prompt = sut.GetPrompt("SystemPrompt");

        prompt.ShouldContain("coding agents");
    }

    [Fact]
    public void Render_SubstitutesVariables()
    {
        var sut = new PromptManager(NullLogger<PromptManager>.Instance);

        var rendered = sut.Render("ArchitectureOverviewPrompt", new Dictionary<string, string>
        {
            ["RepoName"] = "DemoRepo",
            ["RepoSummary"] = "summary-body",
            ["Provider"] = "azure-openai",
            ["Model"] = "gpt-4o"
        });

        rendered.ShouldContain("DemoRepo");
        rendered.ShouldContain("summary-body");
        rendered.ShouldContain("azure-openai");
        rendered.ShouldNotContain("{{RepoName}}");
    }
}
