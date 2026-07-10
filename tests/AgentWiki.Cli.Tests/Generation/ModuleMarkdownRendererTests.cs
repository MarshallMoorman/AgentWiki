using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;

namespace AgentWiki.Cli.Tests.Generation;

public sealed class ModuleMarkdownRendererTests
{
    [Fact]
    public void RenderIndex_DoesNotTruncateModulePurpose()
    {
        var longPurpose =
            "Expose loan-related endpoints and coordinate query execution, response mapping, and exception translation " +
            "across the full LoanView service surface without omitting any of this detail in the index table.";

        var md = ModuleMarkdownRenderer.RenderIndex(
            repoName: "Demo",
            architecture: new ArchitectureDocument { Title = "Arch", Summary = "S" },
            modules:
            [
                new ModuleDocument
                {
                    Id = "loan",
                    Title = "Loan Management",
                    Purpose = longPurpose
                }
            ],
            crossCutting: [],
            stats: new RepoStats(),
            generatedAt: DateTimeOffset.UtcNow,
            correlationId: "abc",
            offline: true);

        md.ShouldContain(longPurpose);
        md.ShouldNotContain("…");
    }

    [Fact]
    public void RenderIndex_DoesNotTruncateCrossCuttingSummary()
    {
        var longSummary =
            "The application centralizes configuration through ASP.NET Core configuration providers, strongly typed options, " +
            "and environment-specific overrides used across the host and feature modules.";

        var md = ModuleMarkdownRenderer.RenderIndex(
            repoName: "Demo",
            architecture: new ArchitectureDocument { Title = "Arch", Summary = "S" },
            modules: [],
            crossCutting:
            [
                new CrossCuttingDocument
                {
                    Id = "configuration",
                    Title = "Configuration",
                    Summary = longSummary
                }
            ],
            stats: new RepoStats(),
            generatedAt: DateTimeOffset.UtcNow,
            correlationId: "abc",
            offline: true);

        md.ShouldContain(longSummary);
        md.ShouldNotContain("…");
    }
}
