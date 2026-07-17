using AgentWiki.App.Infrastructure;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Models;
using AgentWiki.Desktop.ViewModels;
using Moq;

namespace AgentWiki.Desktop.Tests.ViewModels;

/// <summary>
/// Ensures tab Bind/Load paths re-evaluate command CanExecute so buttons enable
/// without requiring an unrelated property change or a second navigation.
/// </summary>
public sealed class CommandCanExecuteRefreshTests
{
    [Fact]
    public void Provider_BindRepo_EnablesTestCommand()
    {
        var configLoader = new Mock<IConfigLoader>();
        var llm = new Mock<ILlmCompletionService>();
        llm.Setup(l => l.CanUseLiveLlm(It.IsAny<AgentWikiConfig>(), It.IsAny<string?>())).Returns(false);

        var vm = new ProviderViewModel(configLoader.Object, llm.Object);
        vm.TestCommand.CanExecute(null).ShouldBeFalse();

        vm.BindRepo(
            "/tmp/repo",
            new AgentWikiConfig { RepoPath = "/tmp/repo", Provider = "openai" });

        vm.TestCommand.CanExecute(null).ShouldBeTrue();

        // Same path again (tab re-entered) must still report executable.
        vm.BindRepo(
            "/tmp/repo",
            new AgentWikiConfig { RepoPath = "/tmp/repo", Provider = "openai" });
        vm.TestCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task Dashboard_LoadAsync_EnablesAnalyzeCommand()
    {
        using var temp = new TempDir("agentwiki-desktop-dash-");
        var configLoader = new Mock<IConfigLoader>();
        configLoader.Setup(c => c.LoadAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentWikiConfig { RepoPath = temp.Path });
        configLoader.Setup(c => c.ApplyCliOverrides(
                It.IsAny<AgentWikiConfig>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .Returns((AgentWikiConfig cfg, string? r, string? o, string? m, string? p) =>
            {
                if (r is not null)
                {
                    cfg.RepoPath = r;
                }

                return cfg;
            });

        var analyzer = new Mock<IRepoAnalyzer>();
        var lastRun = new Mock<ILastRunStore>();
        lastRun.Setup(s => s.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LastRunState?)null);

        var vm = new DashboardViewModel(configLoader.Object, analyzer.Object, lastRun.Object);
        vm.AnalyzeCommand.CanExecute(null).ShouldBeFalse();

        await vm.LoadAsync(temp.Path, new AgentWikiConfig { RepoPath = temp.Path });

        vm.AnalyzeCommand.CanExecute(null).ShouldBeTrue();
        vm.OpenAgentsMdCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void Wiki_BindRepo_EnablesRevealWhenWikiExists()
    {
        using var temp = new TempDir("agentwiki-desktop-wiki-");
        var wikiRoot = Path.Combine(temp.Path, "docs", "wiki");
        Directory.CreateDirectory(wikiRoot);
        File.WriteAllText(Path.Combine(wikiRoot, "index.md"), "# Hello");

        var vm = new WikiBrowserViewModel();
        vm.RevealInOsCommand.CanExecute(null).ShouldBeFalse();
        vm.OpenExternalCommand.CanExecute(null).ShouldBeFalse();

        vm.BindRepo(temp.Path, new AgentWikiConfig { RepoPath = temp.Path, OutputPath = "docs/wiki" });

        vm.RevealInOsCommand.CanExecute(null).ShouldBeTrue();
        vm.OpenExternalCommand.CanExecute(null).ShouldBeFalse(); // no file selected yet
    }

    [Fact]
    public async Task Logs_LoadAsync_EnablesOpenFolder()
    {
        // Ensure logging is configured so LogDirectory is available.
        AgentWikiLogging.Configure(verbose: false, enableConsoleSink: false);

        var vm = new LogsViewModel();
        await vm.LoadAsync();

        vm.OpenFolderCommand.CanExecute(null).ShouldBeTrue();
        vm.LogDirectory.ShouldNotBeNullOrWhiteSpace();
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir(string prefix)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
