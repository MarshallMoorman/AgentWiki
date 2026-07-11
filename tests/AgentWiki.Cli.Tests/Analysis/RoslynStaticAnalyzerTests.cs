using AgentWiki.Core.Analysis;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentWiki.Cli.Tests.Analysis;

public sealed class RoslynStaticAnalyzerTests
{
    private readonly RoslynStaticAnalyzer _sut = new(NullLogger<RoslynStaticAnalyzer>.Instance);

    [Fact]
    public async Task AnalyzeAsync_Disabled_ReturnsSkipped()
    {
        var analysis = CreateMinimalAnalysis("/tmp/empty");
        var result = await _sut.AnalyzeAsync(
            analysis,
            new AgentWikiConfig { EnableRoslynAnalysis = false });

        result.Enabled.ShouldBeFalse();
        result.Succeeded.ShouldBeTrue();
        result.UsedRoslyn.ShouldBeFalse();
        result.PublicTypes.Count.ShouldBe(0);
    }

    [Fact]
    public async Task AnalyzeAsync_NoCSharp_ReturnsEmptyWithoutFailure()
    {
        var root = CreateTempDir();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "readme.md"), "# hi");
            var analysis = new RepoAnalysisResult
            {
                RepoPath = root,
                RepoName = "non-dotnet",
                Files =
                [
                    new RepoFile
                    {
                        RelativePath = "readme.md",
                        AbsolutePath = Path.Combine(root, "readme.md"),
                        Category = FileCategory.Documentation,
                        SizeBytes = 4,
                        Extension = ".md",
                        SelectedForAnalysis = true
                    }
                ],
                Stats = new RepoStats { TotalFiles = 1, SelectedFiles = 1, DetectedLanguages = ["Markdown"] },
                Summary = "non-dotnet",
                DiscoveryMethod = "test"
            };

            var result = await _sut.AnalyzeAsync(analysis, new AgentWikiConfig());

            result.Succeeded.ShouldBeTrue();
            result.FilesAnalyzed.ShouldBe(0);
            result.PublicTypes.Count.ShouldBe(0);
            result.Summary.ShouldContain("No C#", Case.Insensitive);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task AnalyzeAsync_DotNetSample_FindsTypesControllersMinimalApisAndDi()
    {
        var root = CreateTempDir();
        try
        {
            var src = Path.Combine(root, "src", "Web");
            Directory.CreateDirectory(src);
            await File.WriteAllTextAsync(Path.Combine(src, "Web.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(src, "Program.cs"), """
                var builder = WebApplication.CreateBuilder(args);
                builder.Services.AddSingleton<ILoanService, LoanService>();
                builder.Services.AddControllers();
                var app = builder.Build();
                app.MapGet("/health", () => "ok");
                app.MapPost("/loans", () => Results.Ok());
                app.Run();
                """);
            await File.WriteAllTextAsync(Path.Combine(src, "LoansController.cs"), """
                using Microsoft.AspNetCore.Mvc;

                namespace Web;

                [ApiController]
                [Route("api/loans")]
                public class LoansController : ControllerBase
                {
                    [HttpGet]
                    public IActionResult List() => Ok();

                    [HttpGet("{id}")]
                    [Authorize]
                    public IActionResult Get(int id) => Ok(id);

                    [Obsolete("use v2")]
                    [HttpPost]
                    public IActionResult Create() => Ok();
                }

                public interface ILoanService { }
                public sealed class LoanService : ILoanService { }
                """);

            var files = new List<RepoFile>
            {
                MakeFile("src/Web/Web.csproj", Path.Combine(src, "Web.csproj"), FileCategory.Configuration, ".csproj"),
                MakeFile("src/Web/Program.cs", Path.Combine(src, "Program.cs"), FileCategory.SourceCode, ".cs", "C#"),
                MakeFile("src/Web/LoansController.cs", Path.Combine(src, "LoansController.cs"), FileCategory.SourceCode, ".cs", "C#")
            };

            var analysis = new RepoAnalysisResult
            {
                RepoPath = root,
                RepoName = "web-sample",
                Files = files,
                Stats = new RepoStats
                {
                    TotalFiles = files.Count,
                    SelectedFiles = files.Count,
                    DetectedLanguages = ["C#"]
                },
                Summary = "sample",
                DiscoveryMethod = "test"
            };

            var result = await _sut.AnalyzeAsync(
                analysis,
                new AgentWikiConfig
                {
                    EnableRoslynAnalysis = true,
                    MaxSourceFilesForRoslyn = 50,
                    MaxProjectsToAnalyze = 10
                });

            result.Succeeded.ShouldBeTrue();
            result.UsedRoslyn.ShouldBeTrue();
            result.FilesAnalyzed.ShouldBeGreaterThan(0);
            result.PublicTypes.ShouldContain(t => t.Name == "LoansController");
            result.PublicTypes.ShouldContain(t => t.Name == "ILoanService" && t.Kind == "interface");
            result.Endpoints.ShouldContain(e => e.Kind == "controller" && e.Route.Contains("api/loans", StringComparison.OrdinalIgnoreCase));
            result.Endpoints.ShouldContain(e => e.Kind == "minimal-api" && e.Route == "/health");
            result.EntryPoints.ShouldContain(p => p.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase));
            result.DiRegistrations.ShouldContain(d => d.Contains("AddSingleton", StringComparison.Ordinal));
            result.ObsoleteSymbols.Count.ShouldBeGreaterThan(0);
            result.Projects.ShouldContain(p => p.Name == "Web");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task OfflineArchitecture_UsesStaticAnalysisSymbols()
    {
        var root = CreateTempDir();
        try
        {
            var src = Path.Combine(root, "src", "Lib");
            Directory.CreateDirectory(src);
            await File.WriteAllTextAsync(Path.Combine(src, "Lib.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            await File.WriteAllTextAsync(Path.Combine(src, "Program.cs"), "Console.WriteLine();");
            await File.WriteAllTextAsync(Path.Combine(src, "Greeter.cs"), """
                namespace Lib;
                public interface IGreeter { void Hello(); }
                public sealed class Greeter : IGreeter { public void Hello() { } }
                """);

            var files = new List<RepoFile>
            {
                MakeFile("src/Lib/Lib.csproj", Path.Combine(src, "Lib.csproj"), FileCategory.Configuration, ".csproj"),
                MakeFile("src/Lib/Program.cs", Path.Combine(src, "Program.cs"), FileCategory.SourceCode, ".cs", "C#"),
                MakeFile("src/Lib/Greeter.cs", Path.Combine(src, "Greeter.cs"), FileCategory.SourceCode, ".cs", "C#")
            };

            var analysis = new RepoAnalysisResult
            {
                RepoPath = root,
                RepoName = "lib-sample",
                Files = files,
                Stats = new RepoStats
                {
                    TotalFiles = files.Count,
                    SelectedFiles = files.Count,
                    DetectedLanguages = ["C#"],
                    TopFolders = [new FolderStat("src", 3, 100)]
                },
                Summary = "sample",
                DiscoveryMethod = "test"
            };

            analysis.StaticAnalysis = await _sut.AnalyzeAsync(analysis, new AgentWikiConfig());

            var arch = OfflineArchitectureGenerator.Generate(analysis);
            arch.Summary.ShouldContain("public type", Case.Insensitive);
            arch.KeyComponents.ShouldContain(c => c.Name.Contains("Greeter", StringComparison.OrdinalIgnoreCase)
                                                   || c.Name.Contains("IGreeter", StringComparison.OrdinalIgnoreCase)
                                                   || c.Name == "Lib");

            var plan = OfflineModulePlanner.Plan(analysis);
            var module = OfflineModulePlanner.BuildModuleDocument(plan.Modules[0], analysis);
            module.KeyTypes.ShouldContain(t => t.Contains("Greeter", StringComparison.OrdinalIgnoreCase));
            module.EntryPoints.ShouldContain(p => p.Contains("Program.cs", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static RepoAnalysisResult CreateMinimalAnalysis(string path) => new()
    {
        RepoPath = path,
        RepoName = "x",
        Files = [],
        Stats = new RepoStats(),
        Summary = "",
        DiscoveryMethod = "test"
    };

    private static RepoFile MakeFile(
        string relative,
        string absolute,
        FileCategory category,
        string ext,
        string? language = null) =>
        new()
        {
            RelativePath = relative,
            AbsolutePath = absolute,
            Category = category,
            SizeBytes = 10,
            Extension = ext,
            Language = language,
            SelectedForAnalysis = true
        };

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentwiki-roslyn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }
}
