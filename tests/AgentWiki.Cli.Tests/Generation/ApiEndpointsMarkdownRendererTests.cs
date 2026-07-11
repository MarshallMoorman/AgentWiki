using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;

namespace AgentWiki.Cli.Tests.Generation;

public sealed class ApiEndpointsMarkdownRendererTests
{
    [Fact]
    public void Render_Empty_ExplainsNoEndpoints()
    {
        var md = ApiEndpointsMarkdownRenderer.Render("Demo", [], usedRoslyn: false, llmEnriched: false);
        md.ShouldContain("No HTTP or Function endpoints");
        md.ShouldContain("index.md");
    }

    [Fact]
    public void Render_Catalog_IncludesTableAndDetails()
    {
        var endpoints = new List<EndpointInfo>
        {
            new()
            {
                HttpMethod = "GET",
                Route = "/api/loans",
                HandlerName = "LoansController.List",
                Kind = "controller",
                RelativePath = "src/Web/LoansController.cs",
                AuthHints = ["Authorize"],
                Parameters = ["int id"],
                Description = "Lists loans."
            },
            new()
            {
                HttpMethod = "GET",
                Route = "/health",
                HandlerName = "Program.MapGet",
                Kind = "minimal-api",
                RelativePath = "src/Web/Program.cs",
                Description = "Health probe."
            }
        };

        var md = ApiEndpointsMarkdownRenderer.Render("Demo", endpoints, usedRoslyn: true, llmEnriched: true);
        md.ShouldContain("api/loans");
        md.ShouldContain("LoansController.List");
        md.ShouldContain("minimal-api");
        md.ShouldContain("Authorize");
        md.ShouldContain("Lists loans.");
        md.ShouldContain("## Controllers");
        md.ShouldContain("## Minimal APIs");
    }

    [Fact]
    public void EndpointCatalog_Filter_RespectsIncludeAndExclude()
    {
        var endpoints = new List<EndpointInfo>
        {
            new() { HttpMethod = "GET", Route = "/health", HandlerName = "h", Kind = "minimal-api", RelativePath = "a.cs" },
            new() { HttpMethod = "GET", Route = "/api/loans", HandlerName = "l", Kind = "controller", RelativePath = "b.cs" },
            new() { HttpMethod = "GET", Route = "/swagger", HandlerName = "s", Kind = "minimal-api", RelativePath = "c.cs" }
        };

        var filtered = EndpointCatalog.Filter(
            endpoints,
            new AgentWikiConfig
            {
                EnableApiEndpointDocs = true,
                EndpointExcludePatterns = ["/health", "swagger"]
            });

        filtered.Count.ShouldBe(1);
        filtered[0].Route.ShouldBe("/api/loans");

        var included = EndpointCatalog.Filter(
            endpoints,
            new AgentWikiConfig
            {
                EnableApiEndpointDocs = true,
                EndpointIncludePatterns = ["*/api/*", "/api*"]
            });
        // /api/loans matches; /health and /swagger may not depending on pattern — /api* matches route with *
        included.ShouldContain(e => e.Route == "/api/loans");
    }

    [Fact]
    public void EndpointCatalog_AttachToModules_ScopesByRootPath()
    {
        var catalog = new List<EndpointInfo>
        {
            new()
            {
                HttpMethod = "GET",
                Route = "/api/loans",
                HandlerName = "LoansController.List",
                RelativePath = "src/Loans/LoansController.cs",
                ProjectName = "Loans",
                Kind = "controller"
            },
            new()
            {
                HttpMethod = "GET",
                Route = "/api/customers",
                HandlerName = "CustomersController.List",
                RelativePath = "src/Customers/CustomersController.cs",
                ProjectName = "Customers",
                Kind = "controller"
            }
        };

        var modules = new List<ModuleDocument>
        {
            new() { Id = "loans", Title = "Loans" },
            new() { Id = "customers", Title = "Customers" }
        };
        var descriptors = new List<ModuleDescriptor>
        {
            new()
            {
                Id = "loans",
                Name = "Loans",
                RootPaths = ["src/Loans/"],
                RelatedFiles = ["src/Loans/LoansController.cs"]
            },
            new()
            {
                Id = "customers",
                Name = "Customers",
                RootPaths = ["src/Customers/"],
                RelatedFiles = ["src/Customers/CustomersController.cs"]
            }
        };

        EndpointCatalog.AttachToModules(modules, descriptors, catalog);
        modules[0].Endpoints.Count.ShouldBe(1);
        modules[0].Endpoints[0].Route.ShouldBe("/api/loans");
        modules[1].Endpoints.Count.ShouldBe(1);
        modules[1].Endpoints[0].Route.ShouldBe("/api/customers");
    }

    [Fact]
    public void RenderModule_IncludesEndpointsSection()
    {
        var md = ModuleMarkdownRenderer.RenderModule(new ModuleDocument
        {
            Id = "web",
            Title = "Web",
            Purpose = "Host",
            Endpoints =
            [
                new EndpointInfo
                {
                    HttpMethod = "GET",
                    Route = "/api/x",
                    HandlerName = "XController.Get",
                    Kind = "controller",
                    RelativePath = "X.cs",
                    Description = "Gets X."
                }
            ]
        });

        md.ShouldContain("## Endpoints / Public API");
        md.ShouldContain("/api/x");
        md.ShouldContain("api-endpoints.md");
    }
}
