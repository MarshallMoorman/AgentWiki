using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentWiki.Core.Constants;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.Desktop.Services;

/// <summary>
/// Saves non-secret config to <c>.agentwiki/config.json</c> and secrets to <c>.env</c>.
/// Mirrors CLI layering rules for interactive editing.
/// </summary>
public sealed class ConfigEditorService(ILogger<ConfigEditorService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public string GetConfigPath(string repoPath) =>
        Path.Combine(repoPath, AgentWikiConstants.ConfigDirectoryName, AgentWikiConstants.ConfigFileName);

    public string GetEnvPath(string repoPath) => Path.Combine(repoPath, ".env");

    /// <summary>
    /// Writes non-secret fields to config.json (API keys written as empty placeholders only).
    /// </summary>
    public async Task SaveConfigJsonAsync(
        string repoPath,
        AgentWikiConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        var path = GetConfigPath(repoPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Never persist live secrets into config.json from the UI.
        var toWrite = CloneWithoutSecrets(config);
        var json = JsonSerializer.Serialize(toWrite, JsonOptions);
        await File.WriteAllTextAsync(path, json + Environment.NewLine, cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation("Saved config to {Path}", path);
    }

    /// <summary>
    /// Updates or creates <c>.env</c> with secret key/value pairs (masked fields already unmasked by caller).
    /// </summary>
    public async Task SaveEnvSecretsAsync(
        string repoPath,
        string? openAiApiKey,
        string? azureApiKey,
        CancellationToken cancellationToken = default)
    {
        var path = GetEnvPath(repoPath);
        var lines = File.Exists(path)
            ? (await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false)).ToList()
            : [];

        if (!File.Exists(path))
        {
            var example = Path.Combine(repoPath, ".env.example");
            if (File.Exists(example))
            {
                lines = (await File.ReadAllLinesAsync(example, cancellationToken).ConfigureAwait(false)).ToList();
            }
            else
            {
                lines =
                [
                    "# AgentWiki secrets (do not commit)",
                    "AGENTWIKI_OpenAI__ApiKey=",
                    "AGENTWIKI_AzureOpenAI__ApiKey="
                ];
            }
        }

        if (openAiApiKey is not null)
        {
            SetEnvLine(lines, "AGENTWIKI_OpenAI__ApiKey", openAiApiKey);
        }

        if (azureApiKey is not null)
        {
            SetEnvLine(lines, "AGENTWIKI_AzureOpenAI__ApiKey", azureApiKey);
        }

        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            sb.AppendLine(line);
        }

        await File.WriteAllTextAsync(path, sb.ToString(), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Updated secrets in {Path}", path);
    }

    public static string MaskSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return value.Length <= 4 ? "****" : new string('*', Math.Min(value.Length, 12));
    }

    private static AgentWikiConfig CloneWithoutSecrets(AgentWikiConfig source) =>
        new()
        {
            RepoPath = ".",
            OutputPath = source.OutputPath,
            DefaultModel = source.DefaultModel,
            Provider = source.Provider,
            AgentMdPath = source.AgentMdPath,
            MaxFilesToAnalyze = source.MaxFilesToAnalyze,
            EnableIncrementalUpdates = source.EnableIncrementalUpdates,
            LlmTimeoutSeconds = source.LlmTimeoutSeconds,
            MaxLlmSummaryChars = source.MaxLlmSummaryChars,
            IgnorePatterns = [.. source.IgnorePatterns],
            AzureOpenAI = new AzureOpenAiOptions
            {
                Endpoint = source.AzureOpenAI.Endpoint,
                DeploymentName = source.AzureOpenAI.DeploymentName,
                ApiKey = "",
                UseManagedIdentity = source.AzureOpenAI.UseManagedIdentity
            },
            OpenAI = new OpenAiOptions
            {
                Endpoint = source.OpenAI.Endpoint,
                ApiKey = "",
                Model = source.OpenAI.Model
            }
        };

    private static void SetEnvLine(List<string> lines, string key, string value)
    {
        var prefix = key + "=";
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith(key + " =", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = $"{key}={value}";
                return;
            }
        }

        lines.Add($"{key}={value}");
    }
}
