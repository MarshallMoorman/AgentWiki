using System.Text.Json;
using AgentWiki.Cli.Services;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;

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
    public void FlexibleStringList_ReadsMixedArray()
    {
        var json = """{ "items": ["a", { "name": "b" }, 3] }""";
        using var doc = JsonDocument.Parse(json);
        var list = FlexibleStringListConverter.TokenToList(doc.RootElement.GetProperty("items"));
        list.Count.ShouldBe(3);
        list[0].ShouldBe("a");
    }
}
