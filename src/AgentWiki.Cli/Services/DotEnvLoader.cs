using Microsoft.Extensions.Logging;

namespace AgentWiki.Cli.Services;

/// <summary>
/// Parses and applies simple <c>.env</c> files for AgentWiki configuration.
/// </summary>
public static class DotEnvLoader
{
    /// <summary>
    /// Parses <paramref name="envFilePath"/> into a key/value dictionary. Missing files yield an empty map.
    /// Does not mutate the process environment.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseFile(string envFilePath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(envFilePath) || !File.Exists(envFilePath))
        {
            return result;
        }

        foreach (var rawLine in File.ReadLines(envFilePath))
        {
            if (!TryParseLine(rawLine, out var key, out var value))
            {
                continue;
            }

            result[key] = value;
        }

        return result;
    }

    /// <summary>
    /// Writes entries into the process environment.
    /// </summary>
    /// <param name="values">Key/value pairs to apply.</param>
    /// <param name="overrideExisting">When true, overwrites variables already set in the process.</param>
    /// <returns>Number of variables written.</returns>
    public static int ApplyToProcessEnvironment(
        IReadOnlyDictionary<string, string> values,
        bool overrideExisting = false)
    {
        var applied = 0;
        foreach (var (key, value) in values)
        {
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            if (!overrideExisting && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                continue;
            }

            Environment.SetEnvironmentVariable(key, value);
            applied++;
        }

        return applied;
    }

    /// <summary>
    /// Attempts to load <paramref name="envFilePath"/> into the process environment.
    /// By default does not override variables that are already set (shell/CI wins).
    /// Prefer <see cref="ParseFile"/> + config-layer application for AgentWiki priority rules.
    /// </summary>
    /// <returns>Number of variables applied.</returns>
    public static int Load(string envFilePath, ILogger? logger = null, bool overrideExisting = false)
    {
        var parsed = ParseFile(envFilePath);
        if (parsed.Count == 0)
        {
            return 0;
        }

        var applied = ApplyToProcessEnvironment(parsed, overrideExisting);
        logger?.LogDebug(
            "Loaded {Count} variable(s) from {Path} (applied={Applied}, override={Override})",
            parsed.Count,
            envFilePath,
            applied,
            overrideExisting);
        return applied;
    }

    /// <summary>
    /// Loads <c>.env</c> from the repository root if present.
    /// </summary>
    public static int LoadFromRepo(string repoPath, ILogger? logger = null, bool overrideExisting = false)
    {
        var path = Path.Combine(Path.GetFullPath(repoPath), ".env");
        return Load(path, logger, overrideExisting);
    }

    internal static bool TryParseLine(string rawLine, out string key, out string value)
    {
        key = "";
        value = "";

        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
        {
            return false;
        }

        // Support optional "export KEY=value"
        if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
        {
            line = line["export ".Length..].TrimStart();
        }

        var eq = line.IndexOf('=');
        if (eq <= 0)
        {
            return false;
        }

        key = line[..eq].Trim();
        value = line[(eq + 1)..].Trim();

        if (value.Length >= 2
            && ((value.StartsWith('"') && value.EndsWith('"'))
                || (value.StartsWith('\'') && value.EndsWith('\''))))
        {
            value = value[1..^1];
        }

        return !string.IsNullOrEmpty(key);
    }
}
