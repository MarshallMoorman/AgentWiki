using AgentWiki.Desktop.Models;

namespace AgentWiki.Desktop.Tests.Services;

public sealed class UiSettingsStoreTests
{
    [Fact]
    public void RememberRepo_KeepsNewestFirst_AndCapsCount()
    {
        var settings = new UiSettings();
        for (var i = 0; i < 15; i++)
        {
            settings.RememberRepo($"/tmp/repo-{i}");
        }

        settings.RecentRepos.Count.ShouldBe(UiSettings.MaxRecentRepos);
        settings.RecentRepos[0].ShouldEndWith("repo-14");
        settings.LastRepoPath.ShouldEndWith("repo-14");
    }

    [Fact]
    public void RememberRepo_DedupesPaths()
    {
        var settings = new UiSettings();
        settings.RememberRepo("/tmp/a");
        settings.RememberRepo("/tmp/b");
        settings.RememberRepo("/tmp/a");
        settings.RecentRepos.Count.ShouldBe(2);
        settings.RecentRepos[0].ShouldEndWith("a");
    }

    [Fact]
    public void Theme_DefaultsToSystem()
    {
        var settings = new UiSettings();
        settings.Theme.ShouldBe("system");
    }
}
