using AgentWiki.Cli.Services;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentWiki.Cli.Tests.Services;

public sealed class LastRunStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var root = CreateTempDir();
        try
        {
            var sut = new LastRunStore(NullLogger<LastRunStore>.Instance);
            var state = new LastRunState
            {
                CommitSha = "deadbeef",
                TimestampUtc = DateTimeOffset.Parse("2026-07-09T12:00:00Z"),
                CorrelationId = "abc",
                Mode = "generate",
                OutputPath = "docs/wiki",
                FilesWritten = ["index.md", "architecture.md"],
                ModuleIds = ["cli", "core"],
                TotalFiles = 10,
                ToolVersion = "0.5.0"
            };

            await sut.SaveAsync(root, state);
            var loaded = await sut.LoadAsync(root);

            loaded.ShouldNotBeNull();
            loaded!.CommitSha.ShouldBe("deadbeef");
            loaded.ModuleIds.ShouldBe(["cli", "core"]);
            loaded.FilesWritten.Count.ShouldBe(2);
            File.Exists(LastRunStore.GetPath(root)).ShouldBeTrue();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Load_ReturnsNullWhenMissing()
    {
        var root = CreateTempDir();
        try
        {
            var sut = new LastRunStore(NullLogger<LastRunStore>.Instance);
            var loaded = await sut.LoadAsync(root);
            loaded.ShouldBeNull();
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentwiki-test-" + Guid.NewGuid().ToString("N"));
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
            // best-effort
        }
    }
}
