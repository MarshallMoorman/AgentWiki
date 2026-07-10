using System.Text.Json;
using System.Text.Json.Serialization;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Constants;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentWiki.Cli.Services;

/// <summary>
/// Loads AgentWiki configuration with layered priority (highest last / wins):
/// appsettings defaults → process <c>AGENTWIKI_*</c> env → <c>.agentwiki/config.json</c> → repo <c>.env</c> → CLI flags.
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
        var resolvedRepo = PathUtility.ExpandAndResolve(repoPath);
        logger.LogDebug("Loading configuration for repo {RepoPath}", resolvedRepo);

        // 1) Tool defaults from appsettings.json (installation directory).
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

        var configuration = builder.Build();
        var config = new AgentWikiConfig();
        configuration.Bind(config);
        configuration.GetSection("AzureOpenAI").Bind(config.AzureOpenAI);
        configuration.GetSection("OpenAI").Bind(config.OpenAI);

        // 2) Process environment variables (CI/shell). Overwrite appsettings.
        //    Do NOT load .env yet — .env is a higher layer after config.json.
        var processEnv = EnvConfigApplier.CaptureProcessAgentWikiVars();
        EnvConfigApplier.Apply(config, processEnv);
        if (processEnv.Count > 0)
        {
            logger.LogDebug("Applied {Count} AGENTWIKI_* process environment variable(s)", processEnv.Count);
        }

        // 3) .agentwiki/config.json (or explicit --config path). Overwrites process env.
        var candidatePaths = new List<string>();
        if (!string.IsNullOrWhiteSpace(configFilePath))
        {
            candidatePaths.Add(PathUtility.ExpandAndResolve(configFilePath));
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

        // 4) Repo-root .env — highest non-CLI layer (overwrites config.json and process env).
        var envPath = Path.Combine(resolvedRepo, ".env");
        var dotenv = DotEnvLoader.ParseFile(envPath);
        if (dotenv.Count > 0)
        {
            EnvConfigApplier.Apply(config, dotenv);
            // Also export into process env so nested tools / future reads see them.
            // Override=true so .env wins over pre-existing process values for these keys.
            var exported = DotEnvLoader.ApplyToProcessEnvironment(dotenv, overrideExisting: true);
            logger.LogInformation(
                "Loaded {Count} setting(s) from {Path} ({Exported} applied to process environment)",
                dotenv.Count,
                envPath,
                exported);
        }

        config.RepoPath = resolvedRepo;
        logger.LogInformation(
            "Resolved LLM settings: provider={Provider} model={Model} timeout={Timeout}s maxSummaryChars={MaxChars}",
            config.Provider,
            config.DefaultModel,
            config.LlmTimeoutSeconds,
            config.MaxLlmSummaryChars);

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
            clone.RepoPath = PathUtility.ExpandAndResolve(repoPath);
        }

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            clone.OutputPath = PathUtility.ExpandHome(outputPath.Trim());
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

        // Always take file value for bool (JSON presence is intentional).
        target.EnableIncrementalUpdates = fileConfig.EnableIncrementalUpdates;

        // Previously missing — config.json LlmTimeoutSeconds / MaxLlmSummaryChars were ignored.
        if (fileConfig.LlmTimeoutSeconds > 0)
        {
            target.LlmTimeoutSeconds = fileConfig.LlmTimeoutSeconds;
        }

        if (fileConfig.MaxLlmSummaryChars > 0)
        {
            target.MaxLlmSummaryChars = fileConfig.MaxLlmSummaryChars;
        }

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
