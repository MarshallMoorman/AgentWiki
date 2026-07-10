using Microsoft.Extensions.Logging;

namespace AgentWiki.Cli.Services;

/// <summary>
/// Loads a simple <c>.env</c> file into the process environment.
/// Does not override variables that are already set (shell/CI wins).
/// </summary>
public static class DotEnvLoader
{
    /// <summary>
    /// Attempts to load <paramref name="envFilePath"/>. Missing files are ignored.
    /// </summary>
    /// <returns>Number of variables applied.</returns>
    public static int Load(string envFilePath, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(envFilePath) || !File.Exists(envFilePath))
        {
            return 0;
        }

        var applied = 0;
        foreach (var rawLine in File.ReadLines(envFilePath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            // Support optional "export KEY=value"
            if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
            {
                line = line["export ".Length..].TrimStart();
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            if (value.Length >= 2
                && ((value.StartsWith('"') && value.EndsWith('"'))
                    || (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            // Do not clobber existing process environment (CI/shell has priority).
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                continue;
            }

            Environment.SetEnvironmentVariable(key, value);
            applied++;
        }

        logger?.LogDebug("Loaded {Count} variable(s) from {Path}", applied, envFilePath);
        return applied;
    }

    /// <summary>
    /// Loads <c>.env</c> from the repository root if present.
    /// </summary>
    public static int LoadFromRepo(string repoPath, ILogger? logger = null)
    {
        var path = Path.Combine(Path.GetFullPath(repoPath), ".env");
        return Load(path, logger);
    }
}
