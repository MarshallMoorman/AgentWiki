using AgentWiki.App.Infrastructure;
using AgentWiki.App.Services;
using AgentWiki.Core;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentWiki.Cli.Tests.Integration;

/// <summary>
/// Offline end-to-end workspace: init → generate → dry-run → update no-op path.
/// </summary>
public sealed class WorkspaceOfflineIntegrationTests
{
    [Fact]
    public async Task Init_Generate_ProducesSystemWikiAndAgentsMd()
    {
        var workspace = CreateTempDir("ws");
        var memberA = CreateTempDir("ma");
        var memberB = CreateTempDir("mb");
        try
        {
            await SeedMemberRepoAsync(memberA, "ServiceA");
            await SeedMemberRepoAsync(memberB, "ServiceB");

            // Shared package signal
            await File.WriteAllTextAsync(
                Path.Combine(memberA, "src", "ServiceA.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Shared.Contracts" Version="1.0.0" />
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(memberB, "src", "ServiceB.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Shared.Contracts" Version="1.0.0" />
                  </ItemGroup>
                </Project>
                """);

            var loader = new WorkspaceConfigLoader(NullLogger<WorkspaceConfigLoader>.Instance);
            var init = new WorkspaceInitService(loader, NullLogger<WorkspaceInitService>.Instance);
            var initResult = await init.InitializeAsync(workspace, "Demo Workspace", force: true);
            initResult.Success.ShouldBeTrue(initResult.Error);

            // Replace sample members with real local paths
            var configPath = Path.Combine(workspace, ".agentwiki", "workspace.json");
            var config = new WorkspaceConfig
            {
                Name = "Demo Workspace",
                Description = "Integration test workspace",
                OutputPath = "docs/knowledge-base",
                EnsureMemberWikis = true,
                GenerateAgentsMd = true,
                Members =
                [
                    new WorkspaceMember
                    {
                        Id = "service-a",
                        Path = memberA,
                        Label = "Service A",
                        Role = "service"
                    },
                    new WorkspaceMember
                    {
                        Id = "service-b",
                        Path = memberB,
                        Label = "Service B",
                        Role = "service"
                    }
                ]
            };
            await loader.SaveAsync(config, configPath);

            var orchestrator = CreateOrchestrator();
            var load = await loader.LoadAsync(workspace);
            load.Success.ShouldBeTrue(load.Error);

            var output = Path.Combine(workspace, "docs", "knowledge-base");
            var result = await orchestrator.GenerateAsync(new WorkspaceGenerationRequest
            {
                Config = load.Config!,
                WorkspaceRoot = workspace,
                OutputPath = output,
                Force = true,
                DryRun = false,
                Incremental = false
            });

            result.Success.ShouldBeTrue(result.Error);
            result.MemberCount.ShouldBe(2);
            File.Exists(Path.Combine(output, "index.md")).ShouldBeTrue();
            File.Exists(Path.Combine(output, "architecture.md")).ShouldBeTrue();
            File.Exists(Path.Combine(output, "dependency-graph.md")).ShouldBeTrue();
            File.Exists(Path.Combine(output, "data-flows.md")).ShouldBeTrue();
            File.Exists(Path.Combine(output, "ownership.md")).ShouldBeTrue();
            File.Exists(Path.Combine(output, "members", "service-a.md")).ShouldBeTrue();
            File.Exists(Path.Combine(output, "members", "service-b.md")).ShouldBeTrue();
            File.Exists(Path.Combine(workspace, "AGENTS.md")).ShouldBeTrue();
            File.Exists(Path.Combine(workspace, ".agentwiki", "workspace-last-run.json")).ShouldBeTrue();

            // Member wikis ensured
            File.Exists(Path.Combine(memberA, "docs", "wiki", "index.md")).ShouldBeTrue();
            File.Exists(Path.Combine(memberB, "docs", "wiki", "index.md")).ShouldBeTrue();

            var index = await File.ReadAllTextAsync(Path.Combine(output, "index.md"));
            index.ShouldContain("service-a");
            index.ShouldContain("service-b");
            index.ShouldContain("docs/wiki/index.md");

            var memberPage = await File.ReadAllTextAsync(Path.Combine(output, "members", "service-a.md"));
            memberPage.ShouldContain("docs/wiki/architecture.md");

            var agents = await File.ReadAllTextAsync(Path.Combine(workspace, "AGENTS.md"));
            agents.ShouldContain("Start here (workspace)");
            agents.ShouldContain(Constants.AgentsMd.MarkerBegin);
            agents.ShouldContain(Constants.AgentsMd.SelfUpdateSectionHeading);
            agents.ShouldContain("docs/knowledge-base/");

            var dep = await File.ReadAllTextAsync(Path.Combine(output, "dependency-graph.md"));
            dep.ShouldContain("Shared.Contracts");
        }
        finally
        {
            TryDelete(workspace);
            TryDelete(memberA);
            TryDelete(memberB);
        }
    }

    [Fact]
    public async Task Generate_DryRun_DoesNotWriteFiles()
    {
        var workspace = CreateTempDir("ws-dry");
        var memberA = CreateTempDir("ma-dry");
        try
        {
            await SeedMemberRepoAsync(memberA, "DryA");
            // Pre-seed a member wiki so ensure path is lighter
            Directory.CreateDirectory(Path.Combine(memberA, "docs", "wiki"));
            await File.WriteAllTextAsync(Path.Combine(memberA, "docs", "wiki", "index.md"), "# Index\n");
            await File.WriteAllTextAsync(Path.Combine(memberA, "docs", "wiki", "architecture.md"), "# Arch\n");

            var loader = new WorkspaceConfigLoader(NullLogger<WorkspaceConfigLoader>.Instance);
            var init = new WorkspaceInitService(loader, NullLogger<WorkspaceInitService>.Instance);
            await init.InitializeAsync(workspace, "Dry", force: true);

            var configPath = Path.Combine(workspace, ".agentwiki", "workspace.json");
            await loader.SaveAsync(
                new WorkspaceConfig
                {
                    Name = "Dry",
                    EnsureMemberWikis = false,
                    GenerateAgentsMd = true,
                    Members =
                    [
                        new WorkspaceMember { Id = "dry-a", Path = memberA, Label = "Dry A" }
                    ]
                },
                configPath);

            var load = await loader.LoadAsync(workspace);
            var output = Path.Combine(workspace, "docs", "knowledge-base");
            var orchestrator = CreateOrchestrator();
            var result = await orchestrator.GenerateAsync(new WorkspaceGenerationRequest
            {
                Config = load.Config!,
                WorkspaceRoot = workspace,
                OutputPath = output,
                DryRun = true,
                Force = false
            });

            result.Success.ShouldBeTrue(result.Error);
            result.DryRun.ShouldBeTrue();
            Directory.Exists(output).ShouldBeFalse();
            File.Exists(Path.Combine(workspace, "AGENTS.md")).ShouldBeFalse();
            File.Exists(Path.Combine(workspace, ".agentwiki", "workspace-last-run.json")).ShouldBeFalse();
            result.FilesWritten.Count.ShouldBeGreaterThan(0);
        }
        finally
        {
            TryDelete(workspace);
            TryDelete(memberA);
        }
    }

    [Fact]
    public async Task Update_Incremental_NoChanges_IsEfficient()
    {
        var workspace = CreateTempDir("ws-upd");
        var memberA = CreateTempDir("ma-upd");
        try
        {
            await SeedMemberRepoAsync(memberA, "UpdA");
            Directory.CreateDirectory(Path.Combine(memberA, "docs", "wiki"));
            await File.WriteAllTextAsync(Path.Combine(memberA, "docs", "wiki", "index.md"), "# Index\n");
            await File.WriteAllTextAsync(Path.Combine(memberA, "docs", "wiki", "architecture.md"), "# Arch\n");

            var loader = new WorkspaceConfigLoader(NullLogger<WorkspaceConfigLoader>.Instance);
            await loader.SaveAsync(
                new WorkspaceConfig
                {
                    Name = "Upd",
                    EnsureMemberWikis = false,
                    GenerateAgentsMd = true,
                    Members = [new WorkspaceMember { Id = "upd-a", Path = memberA }]
                },
                Path.Combine(workspace, ".agentwiki", "workspace.json"));

            var orchestrator = CreateOrchestrator();
            var load = await loader.LoadAsync(workspace);
            var output = Path.Combine(workspace, "docs", "knowledge-base");

            var first = await orchestrator.GenerateAsync(new WorkspaceGenerationRequest
            {
                Config = load.Config!,
                WorkspaceRoot = workspace,
                OutputPath = output,
                Force = true
            });
            first.Success.ShouldBeTrue(first.Error);

            var second = await orchestrator.GenerateAsync(new WorkspaceGenerationRequest
            {
                Config = load.Config!,
                WorkspaceRoot = workspace,
                OutputPath = output,
                Incremental = true,
                Force = false
            });
            second.Success.ShouldBeTrue(second.Error);
            second.Message.ShouldContain("No workspace changes");
            second.FilesWritten.Count.ShouldBe(0);
        }
        finally
        {
            TryDelete(workspace);
            TryDelete(memberA);
        }
    }

    [Fact]
    public async Task Status_ReportsMemberWikiHealth()
    {
        var workspace = CreateTempDir("ws-st");
        var memberA = CreateTempDir("ma-st");
        try
        {
            await SeedMemberRepoAsync(memberA, "StA");
            var loader = new WorkspaceConfigLoader(NullLogger<WorkspaceConfigLoader>.Instance);
            await loader.SaveAsync(
                new WorkspaceConfig
                {
                    Name = "Status",
                    Members = [new WorkspaceMember { Id = "st-a", Path = memberA }]
                },
                Path.Combine(workspace, ".agentwiki", "workspace.json"));

            var orchestrator = CreateOrchestrator();
            var status = await orchestrator.GetStatusAsync(workspace);
            status.Success.ShouldBeTrue(status.Error);
            status.ResolvedMembers.Count.ShouldBe(1);
            status.WikiStatuses.Count.ShouldBe(1);
            status.WikiStatuses[0].Exists.ShouldBeFalse();
            status.Warnings.ShouldContain(w => w.Contains("missing", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(workspace);
            TryDelete(memberA);
        }
    }

    [Fact]
    public async Task AddMember_AppendsToConfig()
    {
        var workspace = CreateTempDir("ws-add");
        var member = CreateTempDir("m-add");
        try
        {
            var loader = new WorkspaceConfigLoader(NullLogger<WorkspaceConfigLoader>.Instance);
            var init = new WorkspaceInitService(loader, NullLogger<WorkspaceInitService>.Instance);
            await init.InitializeAsync(workspace, "AddTest", force: true);

            // Clear sample members
            await loader.SaveAsync(
                new WorkspaceConfig { Name = "AddTest", Members = [] },
                Path.Combine(workspace, ".agentwiki", "workspace.json"));

            // Empty members fails validation on load — save a minimal valid member first then add
            await loader.SaveAsync(
                new WorkspaceConfig
                {
                    Name = "AddTest",
                    Members = [new WorkspaceMember { Id = "seed", Path = member }]
                },
                Path.Combine(workspace, ".agentwiki", "workspace.json"));

            var add = await init.AddMemberAsync(workspace, member, memberId: "extra", label: "Extra");
            add.Success.ShouldBeTrue(add.Error);

            var load = await loader.LoadAsync(workspace);
            load.Config!.Members.Count.ShouldBe(2);
            load.Config.Members.ShouldContain(m => m.Id == "extra" && m.Label == "Extra");
        }
        finally
        {
            TryDelete(workspace);
            TryDelete(member);
        }
    }

    [Fact]
    public async Task ListAndRemoveMember_UpdatesConfig()
    {
        var workspace = CreateTempDir("ws-list-rm");
        var memberA = CreateTempDir("ma-lr");
        var memberB = CreateTempDir("mb-lr");
        try
        {
            var loader = new WorkspaceConfigLoader(NullLogger<WorkspaceConfigLoader>.Instance);
            var init = new WorkspaceInitService(loader, NullLogger<WorkspaceInitService>.Instance);
            await loader.SaveAsync(
                new WorkspaceConfig
                {
                    Name = "ListRm",
                    Members =
                    [
                        new WorkspaceMember { Id = "a", Path = memberA },
                        new WorkspaceMember { Id = "b", Path = memberB }
                    ]
                },
                Path.Combine(workspace, ".agentwiki", "workspace.json"));

            var list = await init.ListMembersAsync(workspace);
            list.Success.ShouldBeTrue(list.Error);
            list.Members.Count.ShouldBe(2);

            var remove = await init.RemoveMemberAsync(workspace, "a");
            remove.Success.ShouldBeTrue(remove.Error);
            remove.Message.ShouldContain("1 member");

            list = await init.ListMembersAsync(workspace);
            list.Members.Count.ShouldBe(1);
            list.Members.Single().Id.ShouldBe("b");

            var missing = await init.RemoveMemberAsync(workspace, "nope");
            missing.Success.ShouldBeFalse();
            missing.Error.ShouldContain("not found");
        }
        finally
        {
            TryDelete(workspace);
            TryDelete(memberA);
            TryDelete(memberB);
        }
    }

    [Fact]
    public async Task AddMember_WithoutId_DerivesFromPath()
    {
        var workspace = CreateTempDir("ws-add-auto");
        var memberDir = Path.Combine(Path.GetTempPath(), "LoanService-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(memberDir);
        try
        {
            var loader = new WorkspaceConfigLoader(NullLogger<WorkspaceConfigLoader>.Instance);
            var init = new WorkspaceInitService(loader, NullLogger<WorkspaceInitService>.Instance);
            await loader.SaveAsync(
                new WorkspaceConfig
                {
                    Name = "Auto",
                    Members = [new WorkspaceMember { Id = "seed", Path = memberDir }]
                },
                Path.Combine(workspace, ".agentwiki", "workspace.json"));

            var add = await init.AddMemberAsync(workspace, memberDir); // no id
            add.Success.ShouldBeTrue(add.Error);
            add.Message.ShouldContain("derived");

            var load = await loader.LoadAsync(workspace);
            load.Config!.Members.ShouldContain(m =>
                m.Id.StartsWith("loan-service", StringComparison.OrdinalIgnoreCase)
                || m.Id.Contains("loanservice", StringComparison.OrdinalIgnoreCase)
                || m.Path == memberDir);
            // Folder is LoanService-<guid> → id starts with loan-service-…
            load.Config.Members.Count(m => m.Id != "seed").ShouldBe(1);
            var auto = load.Config.Members.Single(m => m.Id != "seed");
            auto.Path.ShouldBe(memberDir);
            auto.Id.ShouldNotBeNullOrWhiteSpace();
        }
        finally
        {
            TryDelete(workspace);
            TryDelete(memberDir);
        }
    }

    private static WorkspaceOrchestrator CreateOrchestrator()
    {
        var arch = new Mock<IArchitectureGenerator>();
        arch.Setup(a => a.GenerateAsync(
                It.IsAny<RepoAnalysisResult>(),
                It.IsAny<AgentWikiConfig>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((RepoAnalysisResult a, AgentWikiConfig _, string? _, string? _, CancellationToken _) =>
                OfflineArchitectureGenerator.Generate(a));

        var llm = new Mock<ILlmCompletionService>();
        llm.Setup(x => x.CanUseLiveLlm(It.IsAny<AgentWikiConfig>(), It.IsAny<string?>())).Returns(false);

        var changeDetector = new Mock<IChangeDetector>();
        changeDetector.Setup(c => c.DetectAsync(
                It.IsAny<string>(),
                It.IsAny<AgentWikiConfig>(),
                It.IsAny<RepoAnalysisResult?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ChangeDetectionResult.Full("test"));

        var analyzer = new RepoAnalyzer(NullLogger<RepoAnalyzer>.Instance);
        var staticAnalyzer = new RoslynStaticAnalyzer(NullLogger<RoslynStaticAnalyzer>.Instance);
        var agentsMd = new AgentsMdGenerator(
            analyzer,
            staticAnalyzer,
            llm.Object,
            NullLogger<AgentsMdGenerator>.Instance);
        var readme = new ReadmeGenerator(analyzer, llm.Object, NullLogger<ReadmeGenerator>.Instance);

        var wikiGenerator = new SemanticWikiGenerator(
            analyzer,
            staticAnalyzer,
            new WikiGenerationOrchestrator(
                arch.Object,
                llm.Object,
                new PromptManager(NullLogger<PromptManager>.Instance),
                new WikiPostProcessor(),
                NullLogger<WikiGenerationOrchestrator>.Instance),
            new MarkdownOutputWriter(NullLogger<MarkdownOutputWriter>.Instance),
            new AgentBootstrapper(NullLogger<AgentBootstrapper>.Instance),
            agentsMd,
            readme,
            changeDetector.Object,
            new LastRunStore(NullLogger<LastRunStore>.Instance),
            new NullRunTelemetry(),
            NullLogger<SemanticWikiGenerator>.Instance);

        var loader = new WorkspaceConfigLoader(NullLogger<WorkspaceConfigLoader>.Instance);
        return new WorkspaceOrchestrator(
            loader,
            new WorkspaceMemberResolver(NullLogger<WorkspaceMemberResolver>.Instance),
            new MemberWikiInspector(),
            new MemberConfigApplier(NullLogger<MemberConfigApplier>.Instance),
            new CrossRepoSignalCollector(NullLogger<CrossRepoSignalCollector>.Instance),
            analyzer,
            new ConfigLoader(NullLogger<ConfigLoader>.Instance),
            new LastRunStore(NullLogger<LastRunStore>.Instance),
            wikiGenerator,
            new MarkdownOutputWriter(NullLogger<MarkdownOutputWriter>.Instance),
            new WorkspaceLastRunStore(NullLogger<WorkspaceLastRunStore>.Instance),
            NullLogger<WorkspaceOrchestrator>.Instance);
    }

    private static async Task SeedMemberRepoAsync(string root, string name)
    {
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(Path.Combine(root, "README.md"), $"# {name}\n");
        await File.WriteAllTextAsync(
            Path.Combine(root, "src", $"{name}.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        await File.WriteAllTextAsync(
            Path.Combine(root, "src", "Program.cs"),
            "Console.WriteLine(\"hi\");\n");
    }

    private static string CreateTempDir(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"aw-ws-{prefix}-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        Directory.CreateDirectory(Path.Combine(path, ".agentwiki"));
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // ignore
        }
    }
}
