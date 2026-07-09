using AgentWiki.Core.Analysis;

namespace AgentWiki.Cli.Tests.Analysis;

public sealed class GitIgnoreMatcherTests
{
    [Fact]
    public void Ignores_BinAndObj_FromExtraPatterns()
    {
        var matcher = new GitIgnoreMatcher(["**/bin/**", "**/obj/**", "**/node_modules/**"]);

        matcher.IsIgnored("src/Foo/bin/Debug/net10.0/Foo.dll").ShouldBeTrue();
        matcher.IsIgnored("src/Foo/obj/project.assets.json").ShouldBeTrue();
        matcher.IsIgnored("node_modules/lodash/index.js").ShouldBeTrue();
        matcher.IsIgnored("src/Foo/Foo.cs").ShouldBeFalse();
    }

    [Fact]
    public void Ignores_GitDirectory()
    {
        var matcher = new GitIgnoreMatcher();
        matcher.IsIgnored(".git/config").ShouldBeTrue();
        matcher.IsIgnored(".git", isDirectory: true).ShouldBeTrue();
    }

    [Fact]
    public void Supports_NegationRules()
    {
        var matcher = new GitIgnoreMatcher();
        matcher.AddRuleSet("", ["*.log", "!important.log"]);

        matcher.IsIgnored("debug.log").ShouldBeTrue();
        matcher.IsIgnored("important.log").ShouldBeFalse();
    }

    [Fact]
    public void Loads_GitIgnoreFile_FromDisk()
    {
        var root = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, ".gitignore"),
                """
                bin/
                *.user
                dist
                """);

            var matcher = new GitIgnoreMatcher();
            matcher.AddGitIgnoreFile(root, Path.Combine(root, ".gitignore"));

            matcher.IsIgnored("bin/Debug/app.dll").ShouldBeTrue();
            matcher.IsIgnored("App.csproj.user").ShouldBeTrue();
            matcher.IsIgnored("dist/index.js").ShouldBeTrue();
            matcher.IsIgnored("src/App.cs").ShouldBeFalse();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void NestedGitIgnore_IsScopedToDirectory()
    {
        var root = CreateTempDir();
        try
        {
            var nested = Path.Combine(root, "packages", "lib");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, ".gitignore"), "generated/\n");

            var matcher = new GitIgnoreMatcher();
            matcher.AddGitIgnoreFile(root, Path.Combine(nested, ".gitignore"));

            matcher.IsIgnored("packages/lib/generated/foo.cs").ShouldBeTrue();
            matcher.IsIgnored("generated/foo.cs").ShouldBeFalse();
            matcher.IsIgnored("packages/lib/src/foo.cs").ShouldBeFalse();
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
