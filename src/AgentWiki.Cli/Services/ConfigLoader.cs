using System.Text.Json;
using System.Text.Json.Serialization;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Constants;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentWiki.Cli.Services;

/// <summary>
/// Loads AgentWiki configuration from appsettings, environment variables,
/// <c>.agentwiki/config.json</c>, and optional explicit config paths.
/// </summary>
public sealed class ConfigLoader(ILogger<ConfigLoader> logger) : IConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <inheritdoc />
    public async Task<AgentWikiConfig> LoadAsync(
        string repoPath,
        string? configFilePath = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedRepo = Path.GetFullPath(repoPath);
        logger.LogDebug("Loading configuration for repo {RepoPath}", resolvedRepo);

        // Base defaults from appsettings.json (tool installation directory) + env vars.
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: AgentWikiConstants.EnvironmentVariablePrefix);

        var configuration = builder.Build();
        var config = new AgentWikiConfig();
        configuration.Bind(config);

        // Nested AzureOpenAI / OpenAI sections may bind via hierarchical env vars.
        configuration.GetSection("AzureOpenAI").Bind(config.AzureOpenAI);
        configuration.GetSection("OpenAI").Bind(config.OpenAI);

        var candidatePaths = new List<string>();
        if (!string.IsNullOrWhiteSpace(configFilePath))
        {
            candidatePaths.Add(Path.GetFullPath(configFilePath));
        }

        candidatePaths.Add(
            Path.Combine(resolvedRepo, AgentWikiConstants.ConfigDirectoryName, AgentWikiConstants.ConfigFileName));

        foreach (var path in candidatePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
            {
                logger.LogDebug("Config file not found: {Path}", path);
                continue;
            }

            logger.LogInformation("Loading config from {Path}", path);
            await MergeJsonFileAsync(config, path, cancellationToken).ConfigureAwait(false);
            break; // First existing explicit/repo config wins for file layer.
        }

        config.RepoPath = resolvedRepo;
        return config;
    }

    /// <inheritdoc />
    public AgentWikiConfig ApplyCliOverrides(
        AgentWikiConfig config,
        string? repoPath = null,
        string? outputPath = null,
        string? model = null,
        string? provider = null)
    {
        // Clone so callers can reuse the original instance safely.
        var json = JsonSerializer.Serialize(config, JsonOptions);
        var clone = JsonSerializer.Deserialize<AgentWikiConfig>(json, JsonOptions)
                    ?? new AgentWikiConfig();

        if (!string.IsNullOrWhiteSpace(repoPath))
        {
            clone.RepoPath = Path.GetFullPath(repoPath);
        }

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            clone.OutputPath = outputPath;
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            clone.DefaultModel = model;
        }

        if (!string.IsNullOrWhiteSpace(provider))
        {
            clone.Provider = provider;
        }

        return clone;
    }

    private static async Task MergeJsonFileAsync(
        AgentWikiConfig target,
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var fileConfig = await JsonSerializer
            .DeserializeAsync<AgentWikiConfig>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (fileConfig is null)
        {
            return;
        }

        // Explicit file values overwrite appsettings/env defaults (non-null / non-default-ish).
        if (!string.IsNullOrWhiteSpace(fileConfig.OutputPath))
        {
            target.OutputPath = fileConfig.OutputPath;
        }

        if (!string.IsNullOrWhiteSpace(fileConfig.DefaultModel))
        {
            target.DefaultModel = fileConfig.DefaultModel;
        }

        if (!string.IsNullOrWhiteSpace(fileConfig.Provider))
        {
            target.Provider = fileConfig.Provider;
        }

        if (!string.IsNullOrWhiteSpace(fileConfig.AgentMdPath))
        {
            target.AgentMdPath = fileConfig.AgentMdPath;
        }

        if (fileConfig.MaxFilesToAnalyze > 0)
        {
            target.MaxFilesToAnalyze = fileConfig.MaxFilesToAnalyze;
        }

        target.EnableIncrementalUpdates = fileConfig.EnableIncrementalUpdates;

        if (fileConfig.IgnorePatterns is { Count: > 0 })
        {
            target.IgnorePatterns = fileConfig.IgnorePatterns;
        }

        MergeAzure(target.AzureOpenAI, fileConfig.AzureOpenAI);
        MergeOpenAi(target.OpenAI, fileConfig.OpenAI);
    }

    private static void MergeAzure(AzureOpenAiOptions target, AzureOpenAiOptions source)
    {
        if (!string.IsNullOrWhiteSpace(source.Endpoint))
        {
            target.Endpoint = source.Endpoint;
        }

        if (!string.IsNullOrWhiteSpace(source.DeploymentName))
        {
            target.DeploymentName = source.DeploymentName;
        }

        if (!string.IsNullOrWhiteSpace(source.ApiKey))
        {
            target.ApiKey = source.ApiKey;
        }

        target.UseManagedIdentity = source.UseManagedIdentity;
    }

    private static void MergeOpenAi(OpenAiOptions target, OpenAiOptions source)
    {
        if (!string.IsNullOrWhiteSpace(source.Endpoint))
        {
            target.Endpoint = source.Endpoint;
        }

        if (!string.IsNullOrWhiteSpace(source.ApiKey))
        {
            target.ApiKey = source.ApiKey;
        }

        if (!string.IsNullOrWhiteSpace(source.Model))
        {
            target.Model = source.Model;
        }
    }
}
