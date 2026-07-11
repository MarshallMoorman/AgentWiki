using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;

namespace AgentWiki.Cli.Tests.Generation;

public sealed class OfflineModulePlannerTests
{
    [Fact]
    public void Plan_PrefersProjectsAndRaisesDefaultCapAboveEight()
    {
        var files = new List<RepoFile>();
        for (var i = 1; i <= 12; i++)
        {
            var dir = $"src/Proj{i}";
            files.Add(Make($"{dir}/Proj{i}.csproj", FileCategory.Configuration, ".csproj"));
            files.Add(Make($"{dir}/Class1.cs", FileCategory.SourceCode, ".cs", "C#"));
        }

        // test project should be deprioritized
        files.Add(Make("tests/Proj1.Tests/Proj1.Tests.csproj", FileCategory.Tests, ".csproj"));
        files.Add(Make("tests/Proj1.Tests/UnitTest1.cs", FileCategory.Tests, ".cs", "C#"));

        var analysis = CreateAnalysis(files);
        var plan = OfflineModulePlanner.Plan(analysis, new AgentWikiConfig { MaxModules = 16 });

        plan.Modules.Count.ShouldBeGreaterThan(8);
        plan.Modules.Count.ShouldBeLessThanOrEqualTo(16);
        // Production projects should appear before test projects
        plan.Modules[0].RootPaths.ShouldContain(r => r.Contains("src/", StringComparison.OrdinalIgnoreCase));
        var firstTestIndex = plan.Modules.FindIndex(m =>
            m.Id.Contains("test", StringComparison.OrdinalIgnoreCase)
            || m.Name.Contains("test", StringComparison.OrdinalIgnoreCase)
            || m.RootPaths.Any(r => r.Contains("test", StringComparison.OrdinalIgnoreCase)));
        if (firstTestIndex >= 0)
        {
            firstTestIndex.ShouldBeGreaterThan(0);
        }

        // With a tight cap, tests are dropped before production projects
        var tight = OfflineModulePlanner.Plan(analysis, new AgentWikiConfig { MaxModules = 8 });
        tight.Modules.Count.ShouldBe(8);
        tight.Modules.ShouldNotContain(m =>
            m.RootPaths.Any(r => r.Contains("tests/", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Plan_RespectsModuleRootsAndGlobs()
    {
        var files = new List<RepoFile>
        {
            Make("src/Api/Api.csproj", FileCategory.Configuration, ".csproj"),
            Make("src/Api/Program.cs", FileCategory.SourceCode, ".cs", "C#"),
            Make("src/Domain/Domain.csproj", FileCategory.Configuration, ".csproj"),
            Make("src/Domain/Entity.cs", FileCategory.SourceCode, ".cs", "C#"),
            Make("tools/Script/Script.csproj", FileCategory.Configuration, ".csproj"),
            Make("tools/Script/Run.cs", FileCategory.SourceCode, ".cs", "C#")
        };

        var analysis = CreateAnalysis(files);
        var plan = OfflineModulePlanner.Plan(analysis, new AgentWikiConfig
        {
            ModuleRoots = ["src/Api"],
            ModuleGlobs = ["src/*/"],
            MaxModules = 10
        });

        plan.Modules.ShouldContain(m => m.RootPaths.Any(r => r.Contains("Api", StringComparison.OrdinalIgnoreCase)));
        plan.Modules.ShouldContain(m => m.RootPaths.Any(r => r.Contains("Domain", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Plan_ParsesSolutionProjects()
    {
        var root = Path.Combine(Path.GetTempPath(), "agentwiki-sln-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "src", "Web"));
        Directory.CreateDirectory(Path.Combine(root, "src", "Core"));
        try
        {
            File.WriteAllText(Path.Combine(root, "src", "Web", "Web.csproj"),
                """<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>""");
            File.WriteAllText(Path.Combine(root, "src", "Core", "Core.csproj"),
                """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>""");
            File.WriteAllText(Path.Combine(root, "App.sln"),
                """
                Microsoft Visual Studio Solution File, Format Version 12.00
                Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Web", "src\Web\Web.csproj", "{11111111-1111-1111-1111-111111111111}"
                EndProject
                Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Core", "src\Core\Core.csproj", "{22222222-2222-2222-2222-222222222222}"
                EndProject
                Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "Solution Items", "Solution Items", "{33333333-3333-3333-3333-333333333333}"
                EndProject
                """);
            File.WriteAllText(Path.Combine(root, "src", "Web", "Program.cs"), "Console.WriteLine();");
            File.WriteAllText(Path.Combine(root, "src", "Core", "Service.cs"), "namespace Core; public class Service {}");

            var files = new List<RepoFile>
            {
                MakeAbs(root, "App.sln", FileCategory.Configuration, ".sln"),
                MakeAbs(root, "src/Web/Web.csproj", FileCategory.Configuration, ".csproj"),
                MakeAbs(root, "src/Web/Program.cs", FileCategory.SourceCode, ".cs", "C#"),
                MakeAbs(root, "src/Core/Core.csproj", FileCategory.Configuration, ".csproj"),
                MakeAbs(root, "src/Core/Service.cs", FileCategory.SourceCode, ".cs", "C#")
            };

            var analysis = CreateAnalysis(files, root);
            var plan = OfflineModulePlanner.Plan(analysis, new AgentWikiConfig());

            plan.Modules.Count.ShouldBeGreaterThanOrEqualTo(2);
            plan.Modules.ShouldContain(m => m.Name.Contains("Web", StringComparison.OrdinalIgnoreCase));
            plan.Modules.ShouldContain(m => m.Name.Contains("Core", StringComparison.OrdinalIgnoreCase));
            // Web project kind from Sdk
            var web = plan.Modules.First(m => m.Name.Contains("Web", StringComparison.OrdinalIgnoreCase));
            web.Summary.ShouldContain("Web", Case.Insensitive);
            web.RelatedFiles.ShouldContain(f => f.Contains("Program.cs", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_NonDotNet_FallsBackToFolders()
    {
        var files = new List<RepoFile>
        {
            Make("app/main.py", FileCategory.SourceCode, ".py", "Python"),
            Make("lib/util.py", FileCategory.SourceCode, ".py", "Python"),
            Make("README.md", FileCategory.Documentation, ".md")
        };
        var analysis = new RepoAnalysisResult
        {
            RepoPath = "/tmp/repo",
            RepoName = "repo",
            Files = files,
            Stats = new RepoStats
            {
                TotalFiles = files.Count,
                SelectedFiles = files.Count,
                TopFolders =
                [
                    new FolderStat("app", 1, 10),
                    new FolderStat("lib", 1, 10)
                ],
                DetectedLanguages = ["Python"]
            },
            Summary = "py",
            DiscoveryMethod = "test"
        };

        var plan = OfflineModulePlanner.Plan(analysis);
        plan.Modules.Count.ShouldBeGreaterThan(0);
        plan.Modules.ShouldContain(m => m.RootPaths.Any(r => r.Contains("app", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Plan_MaxModulesConfigIsHonored()
    {
        var files = new List<RepoFile>();
        for (var i = 1; i <= 10; i++)
        {
            files.Add(Make($"src/P{i}/P{i}.csproj", FileCategory.Configuration, ".csproj"));
            files.Add(Make($"src/P{i}/A.cs", FileCategory.SourceCode, ".cs", "C#"));
        }

        var plan = OfflineModulePlanner.Plan(CreateAnalysis(files), new AgentWikiConfig { MaxModules = 3 });
        plan.Modules.Count.ShouldBe(3);
    }

    private static RepoAnalysisResult CreateAnalysis(List<RepoFile> files, string? repoPath = null) => new()
    {
        RepoPath = repoPath ?? "/tmp/repo",
        RepoName = "repo",
        Files = files,
        Stats = new RepoStats
        {
            TotalFiles = files.Count,
            SelectedFiles = files.Count,
            DetectedLanguages = files.Select(f => f.Language).Where(l => l is not null).Cast<string>().Distinct().ToList(),
            TopFolders = files
                .Select(f => f.RelativePath.Split('/')[0])
                .Where(s => !string.IsNullOrEmpty(s))
                .GroupBy(s => s)
                .Select(g => new FolderStat(g.Key, g.Count(), g.Count() * 10L))
                .ToList()
        },
        Summary = "test",
        DiscoveryMethod = "test"
    };

    private static RepoFile Make(string relative, FileCategory category, string ext, string? language = null) =>
        new()
        {
            RelativePath = relative,
            AbsolutePath = "/tmp/repo/" + relative,
            Category = category,
            SizeBytes = 20,
            Extension = ext,
            Language = language,
            SelectedForAnalysis = true
        };

    private static RepoFile MakeAbs(string root, string relative, FileCategory category, string ext, string? language = null) =>
        new()
        {
            RelativePath = relative,
            AbsolutePath = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)),
            Category = category,
            SizeBytes = 20,
            Extension = ext,
            Language = language,
            SelectedForAnalysis = true
        };
}
