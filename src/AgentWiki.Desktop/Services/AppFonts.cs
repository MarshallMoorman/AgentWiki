using Avalonia.Media;

namespace AgentWiki.Desktop.Services;

/// <summary>
/// Platform-safe typefaces. Avalonia throws if a composite family cannot realize
/// a requested weight (SemiBold/DemiBold) for the primary face — so we use a
/// single known system font per OS instead of comma-separated fallback lists.
/// </summary>
public static class AppFonts
{
    /// <summary>Monospaced UI / markdown code face for this OS.</summary>
    public static FontFamily Mono { get; } = CreateMono();

    /// <summary>Optional body override; null means "leave platform default".</summary>
    public static FontFamily? BodyOverride { get; } = null;

    private static FontFamily CreateMono()
    {
        // One face only — no composites.
        if (OperatingSystem.IsMacOS())
        {
            return new FontFamily("Menlo");
        }

        if (OperatingSystem.IsWindows())
        {
            // Courier New is universally present; Cascadia Mono may not be.
            return new FontFamily("Consolas");
        }

        // Linux: common metric-compatible mono; "monospace" is a generic family.
        return new FontFamily("monospace");
    }
}
