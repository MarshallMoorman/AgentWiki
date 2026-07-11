using AgentWiki.Desktop.Services;
using Avalonia.Styling;

namespace AgentWiki.Desktop.Tests.Services;

public sealed class ThemeServiceTests
{
    [Theory]
    [InlineData(null, "system")]
    [InlineData("", "system")]
    [InlineData("DARK", "dark")]
    [InlineData("Light", "light")]
    [InlineData("auto", "system")]
    [InlineData("default", "system")]
    [InlineData("weird", "system")]
    public void Normalize_MapsPreferences(string? input, string expected)
    {
        ThemeService.Normalize(input).ShouldBe(expected);
    }

    [Fact]
    public void ToVariant_MapsKnownPreferences()
    {
        ThemeService.ToVariant("dark").ShouldBe(ThemeVariant.Dark);
        ThemeService.ToVariant("light").ShouldBe(ThemeVariant.Light);
        ThemeService.ToVariant("system").ShouldBe(ThemeVariant.Default);
    }

    [Fact]
    public void DisplayName_IsUserFriendly()
    {
        ThemeService.DisplayName("dark").ShouldBe("Dark");
        ThemeService.DisplayName("light").ShouldBe("Light");
        ThemeService.DisplayName("system").ShouldBe("System");
    }
}
