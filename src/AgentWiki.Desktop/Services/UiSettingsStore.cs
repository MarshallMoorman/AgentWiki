using System.Text.Json;
using System.Text.Json.Serialization;
using AgentWiki.Core;
using AgentWiki.Desktop.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.Desktop.Services;

/// <summary>Loads and saves desktop UI preferences under <c>~/.agentwiki/</c>.</summary>
public sealed class UiSettingsStore(ILogger<UiSettingsStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Constants.Paths.ConfigDirectoryName,
            "ui-settings.json");

    public async Task<UiSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path))
            {
                return new UiSettings();
            }

            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<UiSettings>(json, JsonOptions) ?? new UiSettings();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load UI settings from {Path}", SettingsPath);
            return new UiSettings();
        }
    }

    public async Task SaveAsync(UiSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            var path = SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(path, json + Environment.NewLine, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save UI settings to {Path}", SettingsPath);
        }
    }
}
