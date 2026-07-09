using AgentWiki.Cli.Services;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentWiki.Cli.Tests.Services;

public sealed class GitChangeDetectorTests
{
    [Fact]
    public async Task DetectAsync_NoBaseline_RequiresFull()
    {
        var store = new Mock<ILastRunStore>();
        store.Setup(s => s.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LastRunState?)null);

        var sut = new GitChangeDetector(store.Object, NullLogger<GitChangeDetector>.Instance);
        var root = CreateTempDir();
        try
        {
            var result = await sut.DetectAsync(root, new AgentWikiConfig());

            result.RequiresFullRegeneration.ShouldBeTrue();
            result.HasBaseline.ShouldBeFalse();
            result.Reason.ShouldContain("last-run");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task DetectAsync_IncrementalDisabled_RequiresFull()
    {
        var store = new Mock<ILastRunStore>();
        store.Setup(s => s.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LastRunState { CommitSha = "abc" });

        var sut = new GitChangeDetector(store.Object, NullLogger<GitChangeDetector>.Instance);
        var root = CreateTempDir();
        try
        {
            var result = await sut.DetectAsync(root, new AgentWikiConfig { EnableIncrementalUpdates = false });

            result.RequiresFullRegeneration.ShouldBeTrue();
            result.Reason.ShouldContain("disabled");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void IncrementalScope_FromChanges_Selective()
    {
        var changes = new ChangeDetectionResult
        {
            HasBaseline = true,
            RequiresFullRegeneration = false,
            NoChanges = false,
            ArchitectureAffected = true,
            AffectedModuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "agentwiki-cli" },
            AffectedCrossCuttingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "testing" },
            Reason = "selective"
        };

        var scope = IncrementalScope.FromChanges(changes);

        scope.IsFull.ShouldBeFalse();
        scope.Architecture.ShouldBeTrue();
        scope.ModuleIds.ShouldContain("agentwiki-cli");
        scope.CrossCuttingIds.ShouldContain("testing");
        scope.AllModules.ShouldBeFalse();
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
