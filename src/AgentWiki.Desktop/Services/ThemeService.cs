using Avalonia;
using Avalonia.Styling;

namespace AgentWiki.Desktop.Services;

/// <summary>
/// Applies dark / light / system theme to the Avalonia application and persists via UiSettings.
/// </summary>
public static class ThemeService
{
    public const string System = "system";
    public const string Dark = "dark";
    public const string Light = "light";

    public static readonly string[] Choices = [System, Dark, Light];

    /// <summary>Normalizes a stored preference to system|dark|light.</summary>
    public static string Normalize(string? preference)
    {
        if (string.IsNullOrWhiteSpace(preference))
        {
            return System;
        }

        return preference.Trim().ToLowerInvariant() switch
        {
            "dark" => Dark,
            "light" => Light,
            "system" or "default" or "auto" => System,
            _ => System
        };
    }

    /// <summary>Maps preference to Avalonia <see cref="ThemeVariant"/>.</summary>
    public static ThemeVariant ToVariant(string? preference) =>
        Normalize(preference) switch
        {
            Dark => ThemeVariant.Dark,
            Light => ThemeVariant.Light,
            _ => ThemeVariant.Default // follow OS
        };

    /// <summary>Applies the preference to <see cref="Application.Current"/> when available.</summary>
    public static void Apply(string? preference)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = ToVariant(preference);
    }

    public static string DisplayName(string preference) => Normalize(preference) switch
    {
        Dark => "Dark",
        Light => "Light",
        _ => "System"
    };
}
