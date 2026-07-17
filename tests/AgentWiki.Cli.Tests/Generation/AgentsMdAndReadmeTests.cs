using AgentWiki.App.Infrastructure;
using AgentWiki.App.Services;
using AgentWiki.Core;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentWiki.Cli.Tests.Generation;

public sealed class AgentsMdAndReadmeTests
{
    [Fact]
    public void OfflineBuilder_IncludesSelfUpdateSection_AndAgentWikiBlock()
    {
        var analysis = MinimalAnalysis("DemoRepo");
        var markdown = AgentsMdOfflineBuilder.Build(
            analysis,
            new AgentWikiConfig(),
            "docs/wiki",
            instructionSources: []);

        markdown.ShouldContain(Constants.AgentsMd.SelfUpdateSectionHeading);
        markdown.ShouldContain("update **AGENTS.md**");
        markdown.ShouldContain("README.md");
        markdown.ShouldContain(Constants.AgentsMd.MarkerBegin);
        markdown.ShouldContain(Constants.AgentsMd.MarkerEnd);
        markdown.ShouldContain("# AGENTS.md — DemoRepo");
    }

    [Fact]
    public void OfflineBuilder_IncludesMigratedInstructions()
    {
        var analysis = MinimalAnalysis("Demo");
        var sources = new List<InstructionSource>
        {
            new()
            {
                RelativePath = ".github/copilot-instructions.md",
                AbsolutePath = "/tmp/x",
                Content = "Always run tests before commit.",
                DeleteAfterMigration = true
            }
        };

        var markdown = AgentsMdOfflineBuilder.Build(analysis, new AgentWikiConfig(), "docs/wiki", sources);
        markdown.ShouldContain("Project-specific instructions (migrated)");
        markdown.ShouldContain("Always run tests before commit.");
        markdown.ShouldContain(Constants.AgentsMd.SelfUpdateSectionHeading);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("short", true)]
    [InlineData("# My App\n\nTODO: replace this template from Visual Studio\n", true)]
    public void ReadmeHeuristics_DetectsGenericTemplates(string content, bool expectedGeneric)
    {
        ReadmeHeuristics.IsGenericContent(content, genericMaxLength: 500).ShouldBe(expectedGeneric);
    }

    [Fact]
    public void ReadmeHeuristics_AcceptsProjectSpecificReadme()
    {
        var content = """
            # AgentWiki

            AgentWiki is a native .NET tool that generates agent-optimized documentation wikis.

            ## Build

            ```bash
            dotnet build AgentWiki.slnx
            dotnet test AgentWiki.slnx
            ```

            ## Docs for agents

            See AGENTS.md and docs/wiki/ for architecture, modules, and coding conventions.
            Configuration lives under .agentwiki/config.json; secrets stay in .env.

            More paragraphs ensure this exceeds the generic-length threshold used by AgentWiki
            when deciding whether a README is a stock template versus a real project document.
            """;
        content.Length.ShouldBeGreaterThan(500);
        ReadmeHeuristics.IsGenericContent(content, genericMaxLength: 500).ShouldBeFalse();
    }

    [Fact]
    public void AgentsMdFileClassifier_TrivialBlockOnly_IsTrivial()
    {
        var block = AgentsMdOfflineBuilder.AgentBootstrapperBlock("docs/wiki");
        AgentsMdFileClassifier.IsTrivialContent(block, trivialMaxLength: 200).ShouldBeTrue();
    }

    [Fact]
    public void AgentsMdFileClassifier_FullDocument_IsNotTrivial()
    {
        var full = AgentsMdOfflineBuilder.Build(
            MinimalAnalysis("X"),
            new AgentWikiConfig(),
            "docs/wiki",
            []);
        AgentsMdFileClassifier.IsTrivialContent(full, trivialMaxLength: 200).ShouldBeFalse();
    }

    [Fact]
    public async Task AgentsMdGenerator_Offline_WritesFullFile_WithSelfUpdate()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "src"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "src", "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "src", "Program.cs"), "Console.WriteLine();\n");

        var llm = new Mock<ILlmCompletionService>();
        llm.Setup(l => l.CanUseLiveLlm(It.IsAny<AgentWikiConfig>(), It.IsAny<string?>())).Returns(false);

        var sut = new AgentsMdGenerator(
            new RepoAnalyzer(NullLogger<RepoAnalyzer>.Instance),
            new RoslynStaticAnalyzer(NullLogger<RoslynStaticAnalyzer>.Instance),
            llm.Object,
            NullLogger<AgentsMdGenerator>.Instance);

        var result = await sut.GenerateAsync(new AgentsMdGenerationRequest
        {
            Config = new AgentWikiConfig { RepoPath = temp.Path },
            RepoPath = temp.Path,
            Force = false,
            DryRun = false
        });

        result.Success.ShouldBeTrue(result.Error);
        result.Action.ShouldBe(AgentsMdAction.Created);
        var path = Path.Combine(temp.Path, "AGENTS.md");
        File.Exists(path).ShouldBeTrue();
        var text = await File.ReadAllTextAsync(path);
        text.ShouldContain(Constants.AgentsMd.SelfUpdateSectionHeading);
        text.ShouldContain(Constants.AgentsMd.MarkerBegin);
        result.UsedOfflineFallback.ShouldBeTrue();
    }

    [Fact]
    public async Task AgentsMdGenerator_DryRun_DoesNotWriteOrDelete_ButReportsMigration()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "src"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "src", "A.cs"), "class A {}");
        var githubDir = Path.Combine(temp.Path, ".github");
        Directory.CreateDirectory(githubDir);
        var copilot = Path.Combine(githubDir, "copilot-instructions.md");
        await File.WriteAllTextAsync(copilot, "Use primary constructors.");

        var llm = new Mock<ILlmCompletionService>();
        llm.Setup(l => l.CanUseLiveLlm(It.IsAny<AgentWikiConfig>(), It.IsAny<string?>())).Returns(false);

        var sut = new AgentsMdGenerator(
            new RepoAnalyzer(NullLogger<RepoAnalyzer>.Instance),
            new RoslynStaticAnalyzer(NullLogger<RoslynStaticAnalyzer>.Instance),
            llm.Object,
            NullLogger<AgentsMdGenerator>.Instance);

        var result = await sut.GenerateAsync(new AgentsMdGenerationRequest
        {
            Config = new AgentWikiConfig { RepoPath = temp.Path, MigrateCopilotInstructions = true },
            RepoPath = temp.Path,
            DryRun = true
        });

        result.Success.ShouldBeTrue(result.Error);
        result.DryRun.ShouldBeTrue();
        File.Exists(Path.Combine(temp.Path, "AGENTS.md")).ShouldBeFalse();
        File.Exists(copilot).ShouldBeTrue();
        result.WouldDeleteFiles.ShouldContain(".github/copilot-instructions.md");
        result.Content.ShouldNotBeNull();
        result.Content!.ShouldContain("Use primary constructors.");
        result.Content.ShouldContain(Constants.AgentsMd.SelfUpdateSectionHeading);
    }

    [Fact]
    public async Task AgentsMdGenerator_MigratesAndDeletesCopilotInstructions()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "src"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "src", "A.cs"), "class A {}");
        var githubDir = Path.Combine(temp.Path, ".github");
        Directory.CreateDirectory(githubDir);
        var copilot = Path.Combine(githubDir, "copilot-instructions.md");
        await File.WriteAllTextAsync(copilot, "Never commit secrets from this project.");

        var llm = new Mock<ILlmCompletionService>();
        llm.Setup(l => l.CanUseLiveLlm(It.IsAny<AgentWikiConfig>(), It.IsAny<string?>())).Returns(false);

        var sut = new AgentsMdGenerator(
            new RepoAnalyzer(NullLogger<RepoAnalyzer>.Instance),
            new RoslynStaticAnalyzer(NullLogger<RoslynStaticAnalyzer>.Instance),
            llm.Object,
            NullLogger<AgentsMdGenerator>.Instance);

        var result = await sut.GenerateAsync(new AgentsMdGenerationRequest
        {
            Config = new AgentWikiConfig { RepoPath = temp.Path, MigrateCopilotInstructions = true },
            RepoPath = temp.Path
        });

        result.Success.ShouldBeTrue(result.Error);
        File.Exists(Path.Combine(temp.Path, "AGENTS.md")).ShouldBeTrue();
        File.Exists(copilot).ShouldBeFalse();
        result.DeletedFiles.ShouldContain(".github/copilot-instructions.md");
        (await File.ReadAllTextAsync(Path.Combine(temp.Path, "AGENTS.md")))
            .ShouldContain("Never commit secrets from this project.");
    }

    [Fact]
    public async Task AgentsMdGenerator_SkipsSubstantialExisting_WithoutForce()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "src"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "src", "A.cs"), "class A {}");
        var rich = AgentsMdOfflineBuilder.Build(
            MinimalAnalysis("Rich", temp.Path),
            new AgentWikiConfig(),
            "docs/wiki",
            []);
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "AGENTS.md"), rich);

        var llm = new Mock<ILlmCompletionService>();
        llm.Setup(l => l.CanUseLiveLlm(It.IsAny<AgentWikiConfig>(), It.IsAny<string?>())).Returns(false);

        var sut = new AgentsMdGenerator(
            new RepoAnalyzer(NullLogger<RepoAnalyzer>.Instance),
            new RoslynStaticAnalyzer(NullLogger<RoslynStaticAnalyzer>.Instance),
            llm.Object,
            NullLogger<AgentsMdGenerator>.Instance);

        var result = await sut.GenerateAsync(new AgentsMdGenerationRequest
        {
            Config = new AgentWikiConfig { RepoPath = temp.Path },
            RepoPath = temp.Path,
            Force = false
        });

        result.Success.ShouldBeTrue();
        result.Action.ShouldBe(AgentsMdAction.Skipped);
    }

    [Fact]
    public async Task ReadmeGenerator_CreatesWhenMissing_LeavesRichAlone()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "src"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "src", "A.cs"), "class A {}");

        var sut = new ReadmeGenerator(
            new RepoAnalyzer(NullLogger<RepoAnalyzer>.Instance),
            NullLogger<ReadmeGenerator>.Instance);

        var created = await sut.GenerateAsync(new ReadmeGenerationRequest
        {
            Config = new AgentWikiConfig { RepoPath = temp.Path },
            RepoPath = temp.Path
        });
        created.Success.ShouldBeTrue(created.Error);
        created.Action.ShouldBe(ReadmeAction.Created);
        File.Exists(Path.Combine(temp.Path, "README.md")).ShouldBeTrue();
        var text = await File.ReadAllTextAsync(Path.Combine(temp.Path, "README.md"));
        text.ShouldContain("AGENTS.md");

        // Replace with rich content (must exceed ReadmeGenericMaxLength and look project-specific)
        var rich = """
            # Real Project

            This is a real project with meaningful documentation for humans and agents.

            ## Build

            ```bash
            dotnet build AgentWiki.slnx
            dotnet test AgentWiki.slnx
            ```

            See AGENTS.md and docs/wiki/ for agent workflows, architecture, and modules.
            Configuration lives in .agentwiki/config.json; never commit secrets from .env.
            Additional deployment notes, contribution guidelines, and release process details
            are documented so this README is clearly a real project document.
            Include environment setup, CI badges, and support contacts when publishing open source.
            """;
        rich.Length.ShouldBeGreaterThan(500);
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "README.md"), rich);

        var skipped = await sut.GenerateAsync(new ReadmeGenerationRequest
        {
            Config = new AgentWikiConfig { RepoPath = temp.Path },
            RepoPath = temp.Path
        });
        skipped.Action.ShouldBe(ReadmeAction.Skipped);
        (await File.ReadAllTextAsync(Path.Combine(temp.Path, "README.md"))).ShouldBe(rich);
    }

    [Fact]
    public async Task ReadmeGenerator_DryRun_DoesNotWrite()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "src"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "src", "A.cs"), "class A {}");

        var sut = new ReadmeGenerator(
            new RepoAnalyzer(NullLogger<RepoAnalyzer>.Instance),
            NullLogger<ReadmeGenerator>.Instance);

        var result = await sut.GenerateAsync(new ReadmeGenerationRequest
        {
            Config = new AgentWikiConfig { RepoPath = temp.Path },
            RepoPath = temp.Path,
            DryRun = true
        });

        result.DryRun.ShouldBeTrue();
        File.Exists(Path.Combine(temp.Path, "README.md")).ShouldBeFalse();
    }

    [Fact]
    public async Task GenerateAsync_MissingAgentsAndReadme_CreatesBoth_FullAgentsHasSelfUpdate()
    {
        var root = Path.Combine(Path.GetTempPath(), "agentwiki-full-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src", "App"));
            await File.WriteAllTextAsync(Path.Combine(root, "src", "App", "App.csproj"), "<Project />\n");
            await File.WriteAllTextAsync(Path.Combine(root, "src", "App", "Program.cs"), "Console.WriteLine();\n");
            Directory.CreateDirectory(Path.Combine(root, ".github"));
            await File.WriteAllTextAsync(
                Path.Combine(root, ".github", "copilot-instructions.md"),
                "Prefer file-scoped namespaces.");

            var analyzer = new RepoAnalyzer(NullLogger<RepoAnalyzer>.Instance);
            var llm = new Mock<ILlmCompletionService>();
            llm.Setup(x => x.CanUseLiveLlm(It.IsAny<AgentWikiConfig>(), It.IsAny<string?>())).Returns(false);
            var arch = new Mock<IArchitectureGenerator>();
            arch.Setup(a => a.GenerateAsync(
                    It.IsAny<RepoAnalysisResult>(),
                    It.IsAny<AgentWikiConfig>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((RepoAnalysisResult a, AgentWikiConfig _, string? _, string? _, CancellationToken _) =>
                    OfflineArchitectureGenerator.Generate(a));

            var changeDetector = new Mock<IChangeDetector>();
            changeDetector.Setup(c => c.DetectAsync(
                    It.IsAny<string>(),
                    It.IsAny<AgentWikiConfig>(),
                    It.IsAny<RepoAnalysisResult?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ChangeDetectionResult.Full("test"));

            var sut = new SemanticWikiGenerator(
                analyzer,
                new RoslynStaticAnalyzer(NullLogger<RoslynStaticAnalyzer>.Instance),
                new WikiGenerationOrchestrator(
                    arch.Object,
                    llm.Object,
                    new PromptManager(NullLogger<PromptManager>.Instance),
                    new WikiPostProcessor(),
                    NullLogger<WikiGenerationOrchestrator>.Instance),
                new MarkdownOutputWriter(NullLogger<MarkdownOutputWriter>.Instance),
                new AgentBootstrapper(NullLogger<AgentBootstrapper>.Instance),
                new AgentsMdGenerator(
                    analyzer,
                    new RoslynStaticAnalyzer(NullLogger<RoslynStaticAnalyzer>.Instance),
                    llm.Object,
                    NullLogger<AgentsMdGenerator>.Instance),
                new ReadmeGenerator(analyzer, NullLogger<ReadmeGenerator>.Instance),
                changeDetector.Object,
                new LastRunStore(NullLogger<LastRunStore>.Instance),
                new NullRunTelemetry(),
                NullLogger<SemanticWikiGenerator>.Instance);

            var output = Path.Combine(root, "docs", "wiki");
            var result = await sut.GenerateAsync(new WikiGenerationRequest
            {
                Config = new AgentWikiConfig
                {
                    OutputPath = "docs/wiki",
                    AgentMdPath = "AGENTS.md",
                    GenerateAgentsMdIfMissing = true,
                    GenerateReadmeIfMissingOrGeneric = true,
                    MigrateCopilotInstructions = true
                },
                RepoPath = root,
                OutputPath = output,
                Force = true
            });

            result.Success.ShouldBeTrue(result.Error);
            File.Exists(Path.Combine(root, "AGENTS.md")).ShouldBeTrue();
            File.Exists(Path.Combine(root, "README.md")).ShouldBeTrue();
            File.Exists(Path.Combine(root, ".github", "copilot-instructions.md")).ShouldBeFalse();
            var agents = await File.ReadAllTextAsync(Path.Combine(root, "AGENTS.md"));
            agents.ShouldContain(Constants.AgentsMd.SelfUpdateSectionHeading);
            agents.ShouldContain("Prefer file-scoped namespaces.");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    private static RepoAnalysisResult MinimalAnalysis(string name, string? path = null) =>
        new()
        {
            RepoPath = path ?? "/tmp/" + name,
            RepoName = name,
            Files =
            [
                new RepoFile
                {
                    RelativePath = "src/App/Program.cs",
                    AbsolutePath = "/tmp/x",
                    Extension = ".cs",
                    Category = FileCategory.SourceCode,
                    SizeBytes = 10,
                    SelectedForAnalysis = true
                }
            ],
            Stats = new RepoStats
            {
                TotalFiles = 1,
                SelectedFiles = 1,
                DetectedLanguages = ["C#"],
                TopFolders = [new FolderStat("src", 1, 10)]
            },
            Summary = "test",
            DiscoveryMethod = "test"
        };

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "agentwiki-agents-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { /* ignore */ }
        }
    }
}
