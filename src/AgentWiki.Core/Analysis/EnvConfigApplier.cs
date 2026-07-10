using AgentWiki.Core.Constants;
using AgentWiki.Core.Models;

namespace AgentWiki.Core.Analysis;

/// <summary>
/// Applies <c>AGENTWIKI_*</c> environment-style key/value pairs onto <see cref="AgentWikiConfig"/>.
/// Nested sections use double underscore (e.g. <c>AGENTWIKI_AzureOpenAI__Endpoint</c>).
/// </summary>
public static class EnvConfigApplier
{
    /// <summary>
    /// Applies every recognized <c>AGENTWIKI_*</c> entry from <paramref name="values"/> onto <paramref name="config"/>.
    /// Later calls overwrite earlier values for the same key.
    /// </summary>
    public static void Apply(AgentWikiConfig config, IEnumerable<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(values);

        foreach (var (rawKey, rawValue) in values)
        {
            if (string.IsNullOrWhiteSpace(rawKey) || rawValue is null)
            {
                continue;
            }

            var key = NormalizeKey(rawKey);
            if (key is null)
            {
                continue;
            }

            ApplyOne(config, key, rawValue);
        }
    }

    /// <summary>
    /// Reads process environment variables that start with <see cref="AgentWikiConstants.EnvironmentVariablePrefix"/>.
    /// </summary>
    public static Dictionary<string, string> CaptureProcessAgentWikiVars()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key?.ToString();
            var value = entry.Value?.ToString();
            if (string.IsNullOrEmpty(key) || value is null)
            {
                continue;
            }

            if (!key.StartsWith(AgentWikiConstants.EnvironmentVariablePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result[key] = value;
        }

        return result;
    }

    private static string? NormalizeKey(string rawKey)
    {
        var key = rawKey.Trim();
        if (!key.StartsWith(AgentWikiConstants.EnvironmentVariablePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // AGENTWIKI_AzureOpenAI__Endpoint → AzureOpenAI:Endpoint (logical path after prefix)
        return key[AgentWikiConstants.EnvironmentVariablePrefix.Length..]
            .Replace("__", ":", StringComparison.Ordinal);
    }

    private static void ApplyOne(AgentWikiConfig config, string key, string value)
    {
        // key is like "Provider", "LlmTimeoutSeconds", "AzureOpenAI:Endpoint", "OpenAI:ApiKey"
        var parts = key.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return;
        }

        if (parts.Length == 1)
        {
            ApplyRoot(config, parts[0], value);
            return;
        }

        if (parts.Length == 2
            && parts[0].Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            ApplyAzure(config.AzureOpenAI, parts[1], value);
            return;
        }

        if (parts.Length == 2
            && parts[0].Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            ApplyOpenAi(config.OpenAI, parts[1], value);
        }
    }

    private static void ApplyRoot(AgentWikiConfig config, string name, string value)
    {
        if (name.Equals("RepoPath", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                config.RepoPath = value;
            }

            return;
        }

        if (name.Equals("OutputPath", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                config.OutputPath = value;
            }

            return;
        }

        if (name.Equals("DefaultModel", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                config.DefaultModel = value;
            }

            return;
        }

        if (name.Equals("Provider", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                config.Provider = value;
            }

            return;
        }

        if (name.Equals("AgentMdPath", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                config.AgentMdPath = value;
            }

            return;
        }

        if (name.Equals("MaxFilesToAnalyze", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(value, out var maxFiles)
            && maxFiles > 0)
        {
            config.MaxFilesToAnalyze = maxFiles;
            return;
        }

        if (name.Equals("EnableIncrementalUpdates", StringComparison.OrdinalIgnoreCase)
            && bool.TryParse(value, out var incremental))
        {
            config.EnableIncrementalUpdates = incremental;
            return;
        }

        if (name.Equals("LlmTimeoutSeconds", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(value, out var timeout)
            && timeout > 0)
        {
            config.LlmTimeoutSeconds = timeout;
            return;
        }

        if (name.Equals("MaxLlmSummaryChars", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(value, out var maxChars)
            && maxChars > 0)
        {
            config.MaxLlmSummaryChars = maxChars;
        }
    }

    private static void ApplyAzure(AzureOpenAiOptions target, string name, string value)
    {
        if (name.Equals("Endpoint", StringComparison.OrdinalIgnoreCase))
        {
            target.Endpoint = value;
            return;
        }

        if (name.Equals("DeploymentName", StringComparison.OrdinalIgnoreCase))
        {
            target.DeploymentName = value;
            return;
        }

        if (name.Equals("ApiKey", StringComparison.OrdinalIgnoreCase))
        {
            target.ApiKey = value;
            return;
        }

        if (name.Equals("UseManagedIdentity", StringComparison.OrdinalIgnoreCase)
            && bool.TryParse(value, out var managed))
        {
            target.UseManagedIdentity = managed;
        }
    }

    private static void ApplyOpenAi(OpenAiOptions target, string name, string value)
    {
        if (name.Equals("Endpoint", StringComparison.OrdinalIgnoreCase))
        {
            target.Endpoint = value;
            return;
        }

        if (name.Equals("ApiKey", StringComparison.OrdinalIgnoreCase))
        {
            target.ApiKey = value;
            return;
        }

        if (name.Equals("Model", StringComparison.OrdinalIgnoreCase))
        {
            target.Model = value;
        }
    }
}
