using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Models;
using AgentWiki.Desktop.ViewModels;
using Moq;

namespace AgentWiki.Desktop.Tests.ViewModels;

public sealed class GenerateViewModelTests
{
    [Fact]
    public async Task Run_UsesWikiGenerator_AndCapturesProgress()
    {
        using var temp = new TempDir();
        var configLoader = new Mock<IConfigLoader>();
        configLoader.Setup(c => c.LoadAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentWikiConfig { RepoPath = temp.Path, OutputPath = "docs/wiki" });
        configLoader.Setup(c => c.ApplyCliOverrides(
                It.IsAny<AgentWikiConfig>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .Returns((AgentWikiConfig cfg, string? r, string? o, string? m, string? p) =>
            {
                if (r is not null) cfg.RepoPath = r;
                if (o is not null) cfg.OutputPath = o;
                return cfg;
            });

        var generator = new Mock<IWikiGenerator>();
        generator.Setup(g => g.GenerateAsync(It.IsAny<WikiGenerationRequest>(), It.IsAny<CancellationToken>()))
            .Returns<WikiGenerationRequest, CancellationToken>((req, ct) =>
            {
                req.Progress?.Report("step 1");
                req.Progress?.Report("step 2");
                return Task.FromResult(GenerationResult.Ok(
                    "done",
                    req.OutputPath,
                    ["index.md"],
                    TimeSpan.FromSeconds(1),
                    warnings: ["soft warning"]));
            });

        var vm = new GenerateViewModel(configLoader.Object, generator.Object);
        vm.BindRepo(temp.Path, new AgentWikiConfig { RepoPath = temp.Path, OutputPath = "docs/wiki" });
        vm.Force = true;
        vm.DryRun = true;

        await vm.RunCommand.ExecuteAsync(null);

        // Allow UI-thread posts from Progress to settle in headless unit tests.
        await Task.Delay(50);

        vm.LastSucceeded.ShouldBe(true);
        vm.ResultMessage.ShouldBe("done");
        vm.FilesWritten.ShouldContain("index.md");
        vm.Warnings.ShouldContain("soft warning");
        generator.Verify(
            g => g.GenerateAsync(
                It.Is<WikiGenerationRequest>(r => r.DryRun && !r.Incremental && r.Force),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Update_SetsIncremental()
    {
        using var temp = new TempDir();
        var configLoader = new Mock<IConfigLoader>();
        configLoader.Setup(c => c.LoadAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentWikiConfig { RepoPath = temp.Path });
        configLoader.Setup(c => c.ApplyCliOverrides(
                It.IsAny<AgentWikiConfig>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .Returns((AgentWikiConfig cfg, string? r, string? o, string? m, string? p) => cfg);

        var generator = new Mock<IWikiGenerator>();
        generator.Setup(g => g.GenerateAsync(It.IsAny<WikiGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenerationResult.Ok("ok", temp.Path, [], TimeSpan.Zero));

        var vm = new UpdateViewModel(configLoader.Object, generator.Object);
        vm.BindRepo(temp.Path, new AgentWikiConfig());
        await vm.RunCommand.ExecuteAsync(null);

        generator.Verify(
            g => g.GenerateAsync(
                It.Is<WikiGenerationRequest>(r => r.Incremental && r.Force),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void BindRepo_EnablesRunCommand_WhenRepoPathSet()
    {
        var configLoader = new Mock<IConfigLoader>();
        var generator = new Mock<IWikiGenerator>();
        var vm = new GenerateViewModel(configLoader.Object, generator.Object);

        vm.RunCommand.CanExecute(null).ShouldBeFalse();
        vm.CancelCommand.CanExecute(null).ShouldBeFalse();

        vm.BindRepo("/tmp/some-repo", new AgentWikiConfig { RepoPath = "/tmp/some-repo", OutputPath = "docs/wiki" });

        vm.RunCommand.CanExecute(null).ShouldBeTrue();
        vm.CancelCommand.CanExecute(null).ShouldBeFalse();
        vm.OutputPath.ShouldBe("docs/wiki");
    }

    [Fact]
    public void BindRepo_ReenablesRunCommand_WhenRepoPathUnchanged()
    {
        // Simulates reopening the Generate tab after repo was already bound:
        // property equality would skip NotifyPropertyChanged, so BindRepo must
        // still force CanExecute refresh.
        var configLoader = new Mock<IConfigLoader>();
        var generator = new Mock<IWikiGenerator>();
        var vm = new GenerateViewModel(configLoader.Object, generator.Object);

        vm.BindRepo("/tmp/repo-a", null);
        vm.RunCommand.CanExecute(null).ShouldBeTrue();

        // Force-disable by clearing path without going through BindRepo, then re-bind same path.
        vm.RepoPath = "";
        vm.RunCommand.CanExecute(null).ShouldBeFalse();

        vm.BindRepo("/tmp/repo-a", null);
        vm.RunCommand.CanExecute(null).ShouldBeTrue();

        // Second bind with identical path (no property change) must still leave command enabled.
        vm.BindRepo("/tmp/repo-a", null);
        vm.RunCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void RefreshCommandStates_ReevaluatesAfterIsRunningToggle()
    {
        var configLoader = new Mock<IConfigLoader>();
        var generator = new Mock<IWikiGenerator>();
        var vm = new GenerateViewModel(configLoader.Object, generator.Object);
        vm.BindRepo("/tmp/repo", null);

        vm.IsRunning = true;
        vm.RunCommand.CanExecute(null).ShouldBeFalse();
        vm.CancelCommand.CanExecute(null).ShouldBeTrue();

        vm.IsRunning = false;
        vm.RunCommand.CanExecute(null).ShouldBeTrue();
        vm.CancelCommand.CanExecute(null).ShouldBeFalse();
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "agentwiki-desktop-gen-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { /* ignore */ }
        }
    }
}
