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
    public void ParseArchitectureJson_AcceptsOutputMarkdownField()
    {
        // 2026-07-17 LoanView: model wrapped full markdown in { "output": "..." }
        var raw = """
            {
              "output": "# Elevate-LMS-LoanView Architecture Overview\n\n## Purpose\n\nElevate-LMS-LoanView is an ASP.NET Core–based loan servicing API. The repository is organized as a multi-project .NET solution centered around a public HTTP API that exposes loan, customer, rewards, and related loan-management functionality.\n\n## Layers\n\n- Controllers\n- Query / Service Layer\n"
            }
            """;

        var doc = ArchitectureGenerator.ParseArchitectureJson(raw);
        doc.FullMarkdown.ShouldNotBeNullOrWhiteSpace();
        doc.FullMarkdown!.ShouldContain("loan servicing");
    }

    [Fact]
    public void ParseArchitectureJson_SalvagesSparseRepoTypeEntrypoints()
    {
        // 2026-07-17 LoanView: tiny sketch instead of full schema
        var raw = """
            {
              "repo": "Elevate-LMS-LoanView",
              "type": "ASP.NET Core Web API",
              "entrypoints": [
                "LoanView/Elevate.Lms.LoanView.Api/Program.cs",
                "LoanView/Elevate.Lms.LoanView.Api/Startup.cs"
              ]
            }
            """;

        var doc = ArchitectureGenerator.ParseArchitectureJson(raw);
        doc.Summary.ShouldContain("ASP.NET Core");
        doc.KeyComponents.Count.ShouldBe(2);
        doc.KeyComponents[0].Path.ShouldContain("Program.cs");
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
