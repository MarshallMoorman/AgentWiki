using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;

namespace AgentWiki.Cli.Tests.Generation;

public sealed class ArchitectureMarkdownRendererTests
{
    [Fact]
    public void Render_IncludesSectionsAndMermaid()
    {
        var doc = new ArchitectureDocument
        {
            Title = "Test Architecture",
            Summary = "Short summary.",
            SystemContext = "Context here.",
            Layers =
            [
                new ArchitectureLayer
                {
                    Name = "CLI",
                    Responsibility = "Commands",
                    KeyPaths = ["src/Cli/"]
                }
            ],
            KeyComponents =
            [
                new ArchitectureComponent { Name = "Program", Path = "src/Cli/Program.cs", Purpose = "Entry" }
            ],
            DataFlows = ["A -> B"],
            Decisions = ["Use SK"],
            Gotchas = ["No secrets"],
            HowToExtend = ["Add commands"],
            MermaidDiagram = "flowchart LR\n  A-->B",
            UsedOfflineFallback = true
        };

        var md = ArchitectureMarkdownRenderer.Render(doc, "demo");

        md.ShouldContain("# Test Architecture");
        md.ShouldContain("Short summary.");
        md.ShouldContain("`src/Cli/`");
        md.ShouldContain("```mermaid");
        md.ShouldContain("inventory");
    }
}
