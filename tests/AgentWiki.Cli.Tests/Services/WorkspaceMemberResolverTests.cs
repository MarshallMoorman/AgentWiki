using AgentWiki.App.Services;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentWiki.Cli.Tests.Services;

public sealed class WorkspaceMemberResolverTests
{
    [Fact]
    public async Task Resolve_LocalPath_Succeeds()
    {
        var workspace = CreateTempDir("ws");
        var memberRepo = CreateTempDir("member");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(memberRepo, "README.md"), "# Member\n");

            var config = new WorkspaceConfig
            {
                Name = "Test",
                WorkspaceRoot = workspace,
                Members =
                [
                    new WorkspaceMember
                    {
                        Id = "m1",
                        Path = memberRepo, // absolute
                        Label = "Member 1"
                    }
                ]
            };

            var resolver = new WorkspaceMemberResolver(NullLogger<WorkspaceMemberResolver>.Instance);
            var resolved = await resolver.ResolveAsync(config, config.Members[0]);

            resolved.Success.ShouldBeTrue(resolved.Error);
            resolved.IsRemote.ShouldBeFalse();
            resolved.AbsolutePath.ShouldBe(Path.GetFullPath(memberRepo));
        }
        finally
        {
            TryDelete(workspace);
            TryDelete(memberRepo);
        }
    }

    [Fact]
    public async Task Resolve_RelativePath_FromWorkspaceRoot()
    {
        var workspace = CreateTempDir("ws-rel");
        var sibling = Path.Combine(Path.GetDirectoryName(workspace)!, "sibling-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(sibling);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(sibling, "x.cs"), "class X;");

            // Place sibling next to workspace and use relative path
            var relative = Path.GetRelativePath(workspace, sibling);
            var config = new WorkspaceConfig
            {
                Name = "Test",
                WorkspaceRoot = workspace,
                Members = [new WorkspaceMember { Id = "sib", Path = relative }]
            };

            var resolver = new WorkspaceMemberResolver(NullLogger<WorkspaceMemberResolver>.Instance);
            var resolved = await resolver.ResolveAsync(config, config.Members[0]);
            resolved.Success.ShouldBeTrue(resolved.Error);
            Directory.Exists(resolved.AbsolutePath).ShouldBeTrue();
        }
        finally
        {
            TryDelete(workspace);
            TryDelete(sibling);
        }
    }

    [Fact]
    public async Task Resolve_MissingPath_ReturnsError()
    {
        var workspace = CreateTempDir("ws-miss");
        try
        {
            var config = new WorkspaceConfig
            {
                Name = "Test",
                WorkspaceRoot = workspace,
                Members = [new WorkspaceMember { Id = "gone", Path = "./does-not-exist" }]
            };

            var resolver = new WorkspaceMemberResolver(NullLogger<WorkspaceMemberResolver>.Instance);
            var resolved = await resolver.ResolveAsync(config, config.Members[0]);
            resolved.Success.ShouldBeFalse();
            resolved.Error.ShouldContain("does not exist");
        }
        finally
        {
            TryDelete(workspace);
        }
    }

    [Fact]
    public void RedactRemote_StripsCredentials()
    {
        var redacted = WorkspaceMemberResolver.RedactRemote("https://user:secret@github.com/org/repo.git");
        redacted.ShouldNotContain("secret");
        redacted.ShouldContain("github.com");
    }

    private static string CreateTempDir(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"aw-{prefix}-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
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
