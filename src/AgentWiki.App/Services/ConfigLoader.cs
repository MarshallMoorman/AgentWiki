using System.Text.Json;
using System.Text.Json.Serialization;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentWiki.App.Services;

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

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
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
            logger.LogInformation(
                "Applied {Count} AGENTWIKI_* process environment variable(s) (timeout now {Timeout}s)",
                processEnv.Count,
                config.LlmTimeoutSeconds);
            logger.LogDebug(
                "Process AGENTWIKI_* keys: {Keys}",
                string.Join(", ", processEnv.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)));
        }

        // 2b) Well-known industry env vars as fill-if-empty fallbacks (OPENAI_API_KEY, etc.).
        //     Never override values already set by AGENTWIKI_* / appsettings.
        ApplyStandardSecretEnvFallbacks(config);

        // 3) .agentwiki/config.json (or explicit --config path).
        //    Only properties *present* in the JSON overwrite lower layers
        //    (missing/commented keys must not reset env vars to class defaults).
        var candidatePaths = new List<string>();
        if (!string.IsNullOrWhiteSpace(configFilePath))
        {
            candidatePaths.Add(PathUtility.ExpandAndResolve(configFilePath));
        }

        candidatePaths.Add(
            Path.Combine(resolvedRepo, Constants.Paths.ConfigDirectoryName, Constants.Paths.ConfigFileName));

        foreach (var path in candidatePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
            {
                logger.LogDebug("Config file not found: {Path}", path);
                continue;
            }

            logger.LogInformation("Loading config from {Path}", path);
            await MergeJsonFileAsync(config, path, cancellationToken).ConfigureAwait(false);
            break;
        }

        // 4) Repo-root .env — highest non-CLI layer (overwrites config.json and process env).
        var envPath = Path.Combine(resolvedRepo, ".env");
        var dotenv = DotEnvLoader.ParseFile(envPath);
        if (dotenv.Count > 0)
        {
            EnvConfigApplier.Apply(config, dotenv);
            var exported = DotEnvLoader.ApplyToProcessEnvironment(dotenv, overrideExisting: true);
            logger.LogInformation(
                "Loaded {Count} setting(s) from {Path} ({Exported} applied to process environment)",
                dotenv.Count,
                envPath,
                exported);
        }

        // Re-apply standard env fallbacks after config.json/.env so empty placeholders
        // in JSON do not leave keys blank when OPENAI_API_KEY is in the shell.
        // .env AGENTWIKI_* already applied above and wins over these fallbacks.
        ApplyStandardSecretEnvFallbacks(config);

        config.RepoPath = resolvedRepo;
        var openAiKeySet = !string.IsNullOrWhiteSpace(config.OpenAI.ApiKey);
        var azureKeySet = !string.IsNullOrWhiteSpace(config.AzureOpenAI.ApiKey)
                          || config.AzureOpenAI.UseManagedIdentity;
        logger.LogInformation(
            "Resolved LLM settings: provider={Provider} model={Model} timeout={Timeout}s maxSummaryChars={MaxChars} openAiKey={OpenAiKey} azureCreds={AzureCreds}",
            config.Provider,
            config.DefaultModel,
            config.LlmTimeoutSeconds,
            config.MaxLlmSummaryChars,
            openAiKeySet ? "set" : "missing",
            azureKeySet ? "set" : "missing");

        return config;
    }

    /// <summary>
    /// Fills empty OpenAI/Azure credential fields from common process environment names
    /// used by OpenAI SDKs and Azure tooling. Does not overwrite non-empty values.
    /// </summary>
    /// <summary>
    /// Public for unit tests; used during config load.
    /// </summary>
    public static void ApplyStandardSecretEnvFallbacks(AgentWikiConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        static string? Get(params string[] names)
        {
            foreach (var name in names)
            {
                var value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }

        if (string.IsNullOrWhiteSpace(config.OpenAI.ApiKey)
            && Get("OPENAI_API_KEY", "OPENAI_KEY") is { } openAiKey)
        {
            config.OpenAI.ApiKey = openAiKey;
        }

        if (string.IsNullOrWhiteSpace(config.OpenAI.Endpoint)
            && Get("OPENAI_BASE_URL", "OPENAI_ENDPOINT", "OPENAI_API_BASE") is { } openAiEndpoint)
        {
            config.OpenAI.Endpoint = openAiEndpoint;
        }

        if (string.IsNullOrWhiteSpace(config.OpenAI.Model)
            && Get("OPENAI_MODEL") is { } openAiModel)
        {
            config.OpenAI.Model = openAiModel;
        }

        if (string.IsNullOrWhiteSpace(config.AzureOpenAI.ApiKey)
            && Get("AZURE_OPENAI_API_KEY", "AZURE_OPENAI_KEY") is { } azureKey)
        {
            config.AzureOpenAI.ApiKey = azureKey;
        }

        if (string.IsNullOrWhiteSpace(config.AzureOpenAI.Endpoint)
            && Get("AZURE_OPENAI_ENDPOINT") is { } azureEndpoint)
        {
            config.AzureOpenAI.Endpoint = azureEndpoint;
        }

        if (string.IsNullOrWhiteSpace(config.AzureOpenAI.DeploymentName)
            && Get("AZURE_OPENAI_DEPLOYMENT", "AZURE_OPENAI_DEPLOYMENT_NAME") is { } azureDeployment)
        {
            config.AzureOpenAI.DeploymentName = azureDeployment;
        }
    }

    /// <inheritdoc />
    public AgentWikiConfig ApplyCliOverrides(
        AgentWikiConfig config,
        string? repoPath = null,
        string? outputPath = null,
        string? model = null,
        string? provider = null)
    {
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

    /// <summary>
    /// Merges only properties that appear in the JSON file. Deserializing into
    /// <see cref="AgentWikiConfig"/> would fill missing ints with class defaults (e.g. 300)
    /// and incorrectly overwrite process-env values.
    /// </summary>
    private static async Task MergeJsonFileAsync(
        AgentWikiConfig target,
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var doc = await JsonDocument
            .ParseAsync(stream, DocumentOptions, cancellationToken)
            .ConfigureAwait(false);

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var root = doc.RootElement;

        if (TryGetString(root, "outputPath", out var outputPath))
        {
            target.OutputPath = outputPath;
        }

        if (TryGetString(root, "defaultModel", out var defaultModel))
        {
            target.DefaultModel = defaultModel;
        }

        if (TryGetString(root, "provider", out var provider))
        {
            target.Provider = provider;
        }

        if (TryGetString(root, "agentMdPath", out var agentMdPath))
        {
            target.AgentMdPath = agentMdPath;
        }

        if (TryGetInt(root, "maxFilesToAnalyze", out var maxFiles) && maxFiles > 0)
        {
            target.MaxFilesToAnalyze = maxFiles;
        }

        if (TryGetBool(root, "enableIncrementalUpdates", out var incremental))
        {
            target.EnableIncrementalUpdates = incremental;
        }

        if (TryGetInt(root, "llmTimeoutSeconds", out var timeout) && timeout > 0)
        {
            target.LlmTimeoutSeconds = timeout;
        }

        if (TryGetInt(root, "maxLlmSummaryChars", out var maxChars) && maxChars > 0)
        {
            target.MaxLlmSummaryChars = maxChars;
        }

        if (TryGetProperty(root, "ignorePatterns", out var ignorePatterns)
            && ignorePatterns.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in ignorePatterns.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String
                    && item.GetString() is { Length: > 0 } pattern)
                {
                    list.Add(pattern);
                }
            }

            if (list.Count > 0)
            {
                target.IgnorePatterns = list;
            }
        }

        if (TryGetProperty(root, "azureOpenAI", out var azure)
            && azure.ValueKind == JsonValueKind.Object)
        {
            MergeAzureFromJson(target.AzureOpenAI, azure);
        }

        if (TryGetProperty(root, "openAI", out var openAi)
            && openAi.ValueKind == JsonValueKind.Object)
        {
            MergeOpenAiFromJson(target.OpenAI, openAi);
        }
    }

    private static void MergeAzureFromJson(AzureOpenAiOptions target, JsonElement source)
    {
        if (TryGetString(source, "endpoint", out var endpoint))
        {
            target.Endpoint = endpoint;
        }

        if (TryGetString(source, "deploymentName", out var deployment))
        {
            target.DeploymentName = deployment;
        }

        if (TryGetString(source, "apiKey", out var apiKey))
        {
            target.ApiKey = apiKey;
        }

        if (TryGetBool(source, "useManagedIdentity", out var managed))
        {
            target.UseManagedIdentity = managed;
        }
    }

    private static void MergeOpenAiFromJson(OpenAiOptions target, JsonElement source)
    {
        if (TryGetString(source, "endpoint", out var endpoint))
        {
            target.Endpoint = endpoint;
        }

        if (TryGetString(source, "apiKey", out var apiKey))
        {
            target.ApiKey = apiKey;
        }

        if (TryGetString(source, "model", out var model))
        {
            target.Model = model;
        }
    }

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetString(JsonElement obj, string name, out string value)
    {
        if (TryGetProperty(obj, name, out var el) && el.ValueKind == JsonValueKind.String)
        {
            value = el.GetString() ?? "";
            return !string.IsNullOrWhiteSpace(value);
        }

        value = "";
        return false;
    }

    private static bool TryGetInt(JsonElement obj, string name, out int value)
    {
        if (TryGetProperty(obj, name, out var el))
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out value))
            {
                return true;
            }

            if (el.ValueKind == JsonValueKind.String
                && int.TryParse(el.GetString(), out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool TryGetBool(JsonElement obj, string name, out bool value)
    {
        if (TryGetProperty(obj, name, out var el))
        {
            if (el.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = el.GetBoolean();
                return true;
            }

            if (el.ValueKind == JsonValueKind.String
                && bool.TryParse(el.GetString(), out value))
            {
                return true;
            }
        }

        value = false;
        return false;
    }
}
