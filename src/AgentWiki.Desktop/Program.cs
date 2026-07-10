using System;
using Avalonia;

namespace AgentWiki.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Use platform UI fonts (SF Pro / Segoe UI). Avoid forcing Inter as the
            // app-wide family — Avalonia.Fonts.Inter does not cover every weight
            // (e.g. DemiBold) and composite "Inter, …" stacks throw on measure.
            .LogToTrace();
}
