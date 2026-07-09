using AgentWiki.Cli.Services;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Constants;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentWiki.Cli.Tests.Services;

public sealed class AgentBootstrapperTests
{
    [Fact]
    public async Task EnsureInstructionsAsync_CreatesAgentsMd()
    {
        var root = CreateTempDir();
        try
        {
            var sut = new AgentBootstrapper(NullLogger<AgentBootstrapper>.Instance);
            var result = await sut.EnsureInstructionsAsync(root, "AGENTS.md", "docs/wiki");

            result.Success.ShouldBeTrue(result.Error);
            result.Action.ShouldBe(BootstrapAction.Created);
            var path = Path.Combine(root, "AGENTS.md");
            File.Exists(path).ShouldBeTrue();
            var text = await File.ReadAllTextAsync(path);
            text.ShouldContain(AgentWikiConstants.AgentsMdMarkerBegin);
            text.ShouldContain("docs/wiki/index.md");
            text.ShouldContain(AgentWikiConstants.AgentsMdMarkerEnd);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task EnsureInstructionsAsync_IsIdempotent()
    {
        var root = CreateTempDir();
        try
        {
            var sut = new AgentBootstrapper(NullLogger<AgentBootstrapper>.Instance);
            await sut.EnsureInstructionsAsync(root, "AGENTS.md", "docs/wiki");
            var second = await sut.EnsureInstructionsAsync(root, "AGENTS.md", "docs/wiki");

            second.Success.ShouldBeTrue(second.Error);
            second.Action.ShouldBe(BootstrapAction.Unchanged);

            var text = await File.ReadAllTextAsync(Path.Combine(root, "AGENTS.md"));
            var beginCount = CountOccurrences(text, AgentWikiConstants.AgentsMdMarkerBegin);
            beginCount.ShouldBe(1);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task EnsureInstructionsAsync_UpdatesExistingBlock()
    {
        var root = CreateTempDir();
        try
        {
            var path = Path.Combine(root, "AGENTS.md");
            await File.WriteAllTextAsync(path,
                """
                # Project agents

                Some notes.

                <!-- BEGIN AGENTWIKI -->
                ## AgentWiki Documentation
                old content
                <!-- END AGENTWIKI -->
                """);

            var sut = new AgentBootstrapper(NullLogger<AgentBootstrapper>.Instance);
            var result = await sut.EnsureInstructionsAsync(root, "AGENTS.md", "docs/wiki");

            result.Action.ShouldBe(BootstrapAction.Updated);
            var text = await File.ReadAllTextAsync(path);
            text.ShouldContain("Some notes.");
            text.ShouldContain("docs/wiki/architecture.md");
            text.ShouldNotContain("old content");
            CountOccurrences(text, AgentWikiConstants.AgentsMdMarkerBegin).ShouldBe(1);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task EnsureInstructionsAsync_PrefersExistingClaudeMdForDefaultPath()
    {
        var root = CreateTempDir();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "CLAUDE.md"), "# Claude\n");
            var sut = new AgentBootstrapper(NullLogger<AgentBootstrapper>.Instance);

            var result = await sut.EnsureInstructionsAsync(root, "AGENTS.md", "docs/wiki");

            result.Success.ShouldBeTrue(result.Error);
            result.TargetPath.ShouldEndWith("CLAUDE.md");
            File.Exists(Path.Combine(root, "CLAUDE.md")).ShouldBeTrue();
            File.Exists(Path.Combine(root, "AGENTS.md")).ShouldBeFalse();
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
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
