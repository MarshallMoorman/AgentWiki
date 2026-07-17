using AgentWiki.Core.Abstractions;
using AgentWiki.Desktop.ViewModels;
using Moq;

namespace AgentWiki.Desktop.Tests.ViewModels;

public sealed class SetupViewModelTests
{
    [Fact]
    public void BindRepo_BuildsPreview_WithoutForce()
    {
        using var temp = new TempDir();
        var existing = Path.Combine(temp.Path, ".agentwiki", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        File.WriteAllText(existing, "{}");

        var init = new Mock<IInitService>();
        var vm = new SetupViewModel(init.Object);
        vm.BindRepo(temp.Path);

        vm.PreviewItems.ShouldNotBeEmpty();
        vm.PreviewItems.Any(p => p.Contains("skip", StringComparison.OrdinalIgnoreCase)).ShouldBeTrue();
    }

    [Fact]
    public async Task RunInit_DelegatesToService()
    {
        using var temp = new TempDir();
        var init = new Mock<IInitService>();
        init.Setup(i => i.InitializeAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InitResult.Ok("Scaffolded.", [".agentwiki/config.json"]));

        var vm = new SetupViewModel(init.Object) { RepoPath = temp.Path, Force = true };
        await vm.RunInitCommand.ExecuteAsync(null);

        vm.Message.ShouldBe("Scaffolded.");
        vm.CreatedFiles.ShouldContain(".agentwiki/config.json");
        init.Verify(i => i.InitializeAsync(It.IsAny<string>(), true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void BindRepo_EnablesRunInitCommand()
    {
        using var temp = new TempDir();
        var init = new Mock<IInitService>();
        var vm = new SetupViewModel(init.Object);

        vm.RunInitCommand.CanExecute(null).ShouldBeFalse();

        vm.BindRepo(temp.Path);

        vm.RunInitCommand.CanExecute(null).ShouldBeTrue();

        // Re-bind same path (no property change) must still leave command enabled.
        vm.BindRepo(temp.Path);
        vm.RunInitCommand.CanExecute(null).ShouldBeTrue();
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "agentwiki-desktop-tests-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

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
