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
    public void EndpointCatalog_SharedApiRoot_ScopesByControllerRelatedFiles()
    {
        // LoanView-style: all controllers under one Api project root
        var catalog = new List<EndpointInfo>
        {
            new()
            {
                HttpMethod = "GET",
                Route = "/api/brands/{brandId}/loans/{loanId}",
                HandlerName = "LoansController.GetLoanAsync",
                RelativePath = "Api/Controllers/LoansController.cs",
                ProjectName = "Api",
                Kind = "controller"
            },
            new()
            {
                HttpMethod = "GET",
                Route = "/api/brands/{brandId}/customers/{id}",
                HandlerName = "CustomersController.GetAsync",
                RelativePath = "Api/Controllers/CustomersController.cs",
                ProjectName = "Api",
                Kind = "controller"
            },
            new()
            {
                HttpMethod = "GET",
                Route = "/api/brands/{brandId}/loans/{loanId}/rewards",
                HandlerName = "RewardsController.GetAsync",
                RelativePath = "Api/Controllers/RewardsController.cs",
                ProjectName = "Api",
                Kind = "controller"
            },
            new()
            {
                HttpMethod = "ANY",
                Route = "/",
                HandlerName = "LoansController.Map",
                RelativePath = "Api/Controllers/LoansController.cs",
                Kind = "minimal-api"
            }
        };

        var filtered = EndpointCatalog.Filter(catalog, new AgentWikiConfig { EnableApiEndpointDocs = true });
        filtered.ShouldNotContain(e => e.HandlerName.EndsWith(".Map", StringComparison.Ordinal));
        filtered.Count.ShouldBe(3);

        var modules = new List<ModuleDocument>
        {
            new() { Id = "loans", Title = "Loan Management", RelatedFiles = ["Api/Controllers/LoansController.cs"] },
            new() { Id = "customers", Title = "Customer Management", RelatedFiles = ["Api/Controllers/CustomersController.cs"] },
            new() { Id = "rewards", Title = "Rewards", RelatedFiles = ["Api/Controllers/RewardsController.cs"] },
            new()
            {
                Id = "configuration-branding",
                Title = "Configuration",
                RelatedFiles = ["Api/appsettings.json"]
            }
        };
        var descriptors = new List<ModuleDescriptor>
        {
            new()
            {
                Id = "loans",
                Name = "Loan Management",
                RootPaths = ["Api/"],
                RelatedFiles = ["Api/Controllers/LoansController.cs"]
            },
            new()
            {
                Id = "customers",
                Name = "Customer Management",
                RootPaths = ["Api/"],
                RelatedFiles = ["Api/Controllers/CustomersController.cs"]
            },
            new()
            {
                Id = "rewards",
                Name = "Rewards",
                RootPaths = ["Api/"],
                RelatedFiles = ["Api/Controllers/RewardsController.cs"]
            },
            new()
            {
                Id = "configuration-branding",
                Name = "Configuration & Branding",
                RootPaths = ["Api/"],
                RelatedFiles = ["Api/appsettings.json"]
            }
        };

        EndpointCatalog.AttachToModules(modules, descriptors, filtered);
        modules[0].Endpoints.Select(e => e.HandlerName).ShouldBe(["LoansController.GetLoanAsync"]);
        modules[1].Endpoints.Select(e => e.HandlerName).ShouldBe(["CustomersController.GetAsync"]);
        modules[2].Endpoints.Select(e => e.HandlerName).ShouldBe(["RewardsController.GetAsync"]);
        modules[3].Endpoints.Count.ShouldBe(0);
    }

    [Fact]
    public void EndpointCatalog_Normalize_ExpandsControllerToken()
    {
        var ep = EndpointCatalog.NormalizeEndpoint(new EndpointInfo
        {
            HttpMethod = "GET",
            Route = "/api/brands/{brandId}/[controller]/{loanId}",
            HandlerName = "LoansController.GetLoanAsync",
            Kind = "controller",
            RelativePath = "LoansController.cs"
        });
        ep.Route.ShouldBe("/api/brands/{brandId}/Loans/{loanId}");
    }

    [Fact]
    public void EndpointCatalog_IsNoise_CatchAllAndBareMap()
    {
        EndpointCatalog.IsNoiseEndpoint(new EndpointInfo
        {
            HttpMethod = "GET",
            Route = "/{**path}",
            HandlerName = "Startup.MapGet",
            Kind = "minimal-api",
            RelativePath = "Startup.cs"
        }).ShouldBeTrue();

        EndpointCatalog.IsNoiseEndpoint(new EndpointInfo
        {
            HttpMethod = "ANY",
            Route = "/",
            HandlerName = "LoansController.Map",
            Kind = "minimal-api",
            RelativePath = "LoansController.cs"
        }).ShouldBeTrue();
    }

    [Fact]
    public void RenderModule_IncludesEndpointsSection_WithoutDuplicateBullets()
    {
        var md = ModuleMarkdownRenderer.RenderModule(new ModuleDocument
        {
            Id = "web",
            Title = "Web",
            Purpose = "primary: Host the API.; responsibilities: Wire DI.",
            RelatedFiles = ["path: src/Web/Program.cs; role: Entry point"],
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
        md.ShouldContain("Gets X.");
        // Single table presentation — no bullet dump of the same route
        md.ShouldNotContain("- `GET /api/x`");
        // Cleaned purpose / backfilled entry points
        md.ShouldContain("Host the API");
        md.ShouldContain("Program.cs");
    }

    [Fact]
    public void LlmTextCleanup_SplitsFieldDumps()
    {
        var cleaned = LlmTextCleanup.CleanProse(
            "primary: Expose rewards.; responsibilities: Handle HTTP requests.");
        cleaned.ShouldContain("Expose rewards");
        cleaned.ShouldContain("Handle HTTP requests");

        LlmTextCleanup.ExtractPathFromRelatedFile(
                "path: LoanView/Api/Controllers/RewardsController.cs; role: Entry")
            .ShouldBe("LoanView/Api/Controllers/RewardsController.cs");
    }
}
