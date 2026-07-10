using AgentWiki.Core.Analysis;

namespace AgentWiki.Cli.Tests.Analysis;

public sealed class PathUtilityTests
{
    [Fact]
    public void ExpandHome_ExpandsTildeSlash()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var expanded = PathUtility.ExpandHome("~/dev/repo");
        expanded.ShouldBe(Path.Combine(home, "dev/repo"));
    }

    [Fact]
    public void ExpandHome_ExpandsBareTilde()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        PathUtility.ExpandHome("~").ShouldBe(home);
    }

    [Fact]
    public void ExpandHome_LeavesRelativeUnchanged()
    {
        PathUtility.ExpandHome("./foo").ShouldBe("./foo");
        PathUtility.ExpandHome("docs/wiki").ShouldBe("docs/wiki");
    }

    [Fact]
    public void ExpandAndResolve_ResolvesTildeToExistingStylePath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var resolved = PathUtility.ExpandAndResolve("~/.");
        resolved.ShouldBe(Path.GetFullPath(home));
    }

    [Fact]
    public void ToRepoRelative_ConvertsAbsoluteUnderRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), "agentwiki-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var abs = Path.Combine(root, "src", "Foo.cs");
            PathUtility.ToRepoRelative(root, abs).Replace('\\', '/').ShouldBe("src/Foo.cs");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ToRepoRelative_LeavesRelativePaths()
    {
        PathUtility.ToRepoRelative("/tmp/repo", "Policies/policy.xml").ShouldBe("Policies/policy.xml");
    }

    [Fact]
    public void RepoRootDisplayPath_IsPortable()
    {
        PathUtility.RepoRootDisplayPath.ShouldBe(".");
    }
}
