using System.Text.Json;
using AgentWiki.App.Services;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;
// ArchitectureMarkdownRenderer

namespace AgentWiki.Cli.Tests.Generation;

public sealed class LlmJsonTests
{
    [Fact]
    public void ParseModuleDocument_AcceptsObjectPurposeAndObjectDependencies()
    {
        var raw = """
            {
              "id": "loans",
              "title": "Loans",
              "purpose": { "text": "Handles loan origination and servicing." },
              "entryPoints": ["src/Loans/Program.cs"],
              "dependencies": {
                "customers": "Customer API",
                "shared": "Shared kernel"
              },
              "keyTypes": ["LoanService"],
              "howToExtend": ["Add handlers under Services/"],
              "gotchas": ["Watch concurrency"],
              "relatedFiles": ["src/Loans/LoanService.cs"]
            }
            """;

        var doc = WikiGenerationOrchestrator.ParseModuleDocument(
            raw,
            new ModuleDescriptor { Id = "loans", Name = "Loans", Summary = "fallback" });

        doc.Purpose.ShouldContain("loan", Case.Insensitive);
        doc.Dependencies.Count.ShouldBeGreaterThan(0);
        doc.EntryPoints.ShouldContain("src/Loans/Program.cs");
    }

    [Fact]
    public void ParseArchitectureJson_AcceptsAlternateFieldNames()
    {
        var raw = """
            {
              "title": "System Architecture",
              "overview": "A lending platform.",
              "context": "Hosts APIs and workers.",
              "dataFlows": ["Request -> API -> DB"],
              "decisions": ["Use modular monolith"],
              "layers": [
                { "name": "API", "responsibility": "HTTP endpoints", "keyPaths": ["src/Api/"] }
              ]
            }
            """;

        var doc = ArchitectureGenerator.ParseArchitectureJson(raw);

        doc.Summary.ShouldContain("lending", Case.Insensitive);
        doc.SystemContext.ShouldContain("APIs", Case.Insensitive);
        doc.DataFlows.Count.ShouldBe(1);
        doc.Layers.Count.ShouldBe(1);
    }

    [Fact]
    public void ParseArchitectureJson_AcceptsArchitectureOverviewMarkdownBlob()
    {
        // Real-world gpt-chat-latest shape from Elevate-LMS-LoanView run.
        var raw = """
            {
              "repository": "Elevate-LMS-LoanView",
              "architecture_overview": "# Elevate-LMS-LoanView Architecture Overview\n\n## System Context\n\nElevate-LMS-LoanView is an ASP.NET Core Web API that exposes loan servicing and customer-facing loan data from the Elevate Loan Management System (LMS). The API acts primarily as an orchestration and translation layer between HTTP clients and downstream LMS/domain services.\n\nPrimary API domains visible from the repository:\n- Loans\n- Customers\n- Rewards\n"
            }
            """;

        var doc = ArchitectureGenerator.ParseArchitectureJson(raw);

        doc.FullMarkdown.ShouldNotBeNullOrWhiteSpace();
        doc.FullMarkdown!.ShouldContain("ASP.NET Core");
        doc.Title.ShouldContain("Elevate-LMS-LoanView");
        doc.Summary.ShouldNotBeNullOrWhiteSpace();

        var md = ArchitectureMarkdownRenderer.Render(doc, "Elevate-LMS-LoanView");
        md.ShouldContain("ASP.NET Core");
        md.ShouldContain("current");
    }

    [Fact]
    public void FlexibleStringList_ReadsMixedArray()
    {
        var json = """{ "items": ["a", { "name": "b" }, 3] }""";
        using var doc = JsonDocument.Parse(json);
        var list = FlexibleStringListConverter.TokenToList(doc.RootElement.GetProperty("items"));
        list.Count.ShouldBe(3);
        list[0].ShouldBe("a");
    }
}
