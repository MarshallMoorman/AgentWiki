using AgentWiki.Core.Generation;

namespace AgentWiki.Cli.Tests.Generation;

public sealed class RepoWebLinkBuilderTests
{
    [Theory]
    [InlineData("https://github.com/org/Repo.git", "https://github.com/org/Repo")]
    [InlineData("git@github.com:org/Repo.git", "https://github.com/org/Repo")]
    [InlineData("ssh://git@github.com/org/Repo.git", "https://github.com/org/Repo")]
    [InlineData(
        "https://dev.azure.com/org/project/_git/MyRepo",
        "https://dev.azure.com/org/project/_git/MyRepo")]
    public void NormalizeRemoteToHttps(string input, string expected)
    {
        RepoWebLinkBuilder.NormalizeRemoteToHttps(input).ShouldBe(expected);
    }

    [Fact]
    public void Build_GitHub_BlobUrls()
    {
        var links = RepoWebLinkBuilder.Build(
            "git@github.com:org/LoanView.git",
            "feature/x",
            wikiRelativePath: "docs/wiki");

        links.Hosting.ShouldBe("github");
        links.RepoUrl.ShouldBe("https://github.com/org/LoanView");
        links.Branch.ShouldBe("feature/x");
        links.WikiIndexWebUrl.ShouldBe(
            "https://github.com/org/LoanView/blob/feature/x/docs/wiki/index.md");
        links.FileUrl("docs/wiki/architecture.md")
            .ShouldBe("https://github.com/org/LoanView/blob/feature/x/docs/wiki/architecture.md");
    }

    [Fact]
    public void Build_AzureDevOps_BrowseUrls()
    {
        var links = RepoWebLinkBuilder.Build(
            "https://dev.azure.com/contoso/LMS/_git/LoanView",
            "main",
            wikiRelativePath: "docs/wiki");

        links.Hosting.ShouldBe("azure-devops");
        links.WikiIndexWebUrl.ShouldNotBeNull();
        links.WikiIndexWebUrl!.ShouldContain("dev.azure.com");
        links.WikiIndexWebUrl.ShouldContain("version=GBmain");
        links.WikiIndexWebUrl.ShouldContain("path=");
        links.WikiIndexWebUrl.ShouldContain("docs");
    }

    [Fact]
    public void Build_MissingRemote_WarningsOnly()
    {
        var links = RepoWebLinkBuilder.Build(null, "main", "docs/wiki");
        links.RepoUrl.ShouldBeNull();
        links.WikiIndexWebUrl.ShouldBeNull();
        links.Warnings.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void DetectHosting()
    {
        RepoWebLinkBuilder.DetectHosting("https://github.com/a/b").ShouldBe("github");
        RepoWebLinkBuilder.DetectHosting("https://dev.azure.com/o/p/_git/r").ShouldBe("azure-devops");
        RepoWebLinkBuilder.DetectHosting("https://git.example.com/a/b").ShouldBe("unknown");
    }
}
