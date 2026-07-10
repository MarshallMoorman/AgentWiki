namespace AgentWiki.Desktop.Models;

/// <summary>Sidebar navigation entry with glyph + labels.</summary>
public sealed class NavItem
{
    public required NavPage Page { get; init; }
    public required string Title { get; init; }
    public required string Glyph { get; init; }
    public string? Hint { get; init; }

    public static IReadOnlyList<NavItem> DefaultItems { get; } =
    [
        new() { Page = NavPage.Dashboard, Title = "Dashboard", Glyph = "◆", Hint = "Status & inventory" },
        new() { Page = NavPage.Generate, Title = "Generate", Glyph = "✦", Hint = "Full wiki build" },
        new() { Page = NavPage.Update, Title = "Update", Glyph = "↻", Hint = "Incremental git" },
        new() { Page = NavPage.Setup, Title = "Setup", Glyph = "▣", Hint = "Init scaffold" },
        new() { Page = NavPage.Settings, Title = "Settings", Glyph = "⚙", Hint = "Config & secrets" },
        new() { Page = NavPage.Provider, Title = "Provider", Glyph = "◎", Hint = "Test LLM" },
        new() { Page = NavPage.Wiki, Title = "Wiki", Glyph = "☰", Hint = "Browse docs" },
        new() { Page = NavPage.Logs, Title = "Logs", Glyph = "≡", Hint = "Diagnostics" },
    ];
}
