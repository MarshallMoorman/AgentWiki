using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.App.Infrastructure;

/// <summary>
/// Lightweight optional Application Insights telemetry via REST ingestion.
/// Enabled only when <see cref="AgentWikiConfig.ApplicationInsightsConnectionString"/> is set.
/// Avoids a hard dependency on the full AI SDK in the CLI tool package.
/// </summary>
public sealed class ApplicationInsightsRunTelemetry(ILogger<ApplicationInsightsRunTelemetry> logger) : IRunTelemetry
{
    private string? _connectionString;
    private string? _ingestionEndpoint;
    private string? _instrumentationKey;

    /// <inheritdoc />
    public bool IsEnabled => !string.IsNullOrWhiteSpace(_instrumentationKey);

    /// <summary>Configures the sink from the active AgentWiki config (safe to call repeatedly).</summary>
    public void Configure(AgentWikiConfig config)
    {
        var cs = config.ApplicationInsightsConnectionString?.Trim();
        if (string.IsNullOrWhiteSpace(cs))
        {
            _connectionString = null;
            _ingestionEndpoint = null;
            _instrumentationKey = null;
            return;
        }

        if (string.Equals(_connectionString, cs, StringComparison.Ordinal))
        {
            return;
        }

        _connectionString = cs;
        ParseConnectionString(cs, out _ingestionEndpoint, out _instrumentationKey);
        if (IsEnabled)
        {
            logger.LogInformation(
                "Application Insights telemetry enabled (endpoint={Endpoint})",
                _ingestionEndpoint ?? "(default)");
        }
        else
        {
            logger.LogWarning("Application Insights connection string present but InstrumentationKey missing.");
        }
    }

    /// <inheritdoc />
    public void TrackRun(WikiGenerationRequest request, GenerationResult result)
    {
        Configure(request.Config);
        if (!IsEnabled || _instrumentationKey is null)
        {
            return;
        }

        try
        {
            // Fire-and-forget; never fail the wiki run for telemetry.
            _ = Task.Run(async () =>
            {
                try
                {
                    await SendEventAsync(request, result).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Application Insights track failed");
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Application Insights schedule failed");
        }
    }

    private async Task SendEventAsync(WikiGenerationRequest request, GenerationResult result)
    {
        var endpoint = string.IsNullOrWhiteSpace(_ingestionEndpoint)
            ? "https://dc.services.visualstudio.com/v2/track"
            : _ingestionEndpoint.TrimEnd('/') + "/v2/track";

        var name = result.Success ? "AgentWiki.Run.Success" : "AgentWiki.Run.Failure";
        var payload = new
        {
            name = "Microsoft.ApplicationInsights.Event",
            time = DateTime.UtcNow.ToString("O"),
            iKey = _instrumentationKey,
            tags = new Dictionary<string, string>
            {
                ["ai.cloud.role"] = "agent-wiki",
                ["ai.operation.id"] = result.CorrelationId ?? request.CorrelationId
            },
            data = new
            {
                baseType = "EventData",
                baseData = new
                {
                    ver = 2,
                    name,
                    properties = new Dictionary<string, string?>
                    {
                        ["correlationId"] = result.CorrelationId ?? request.CorrelationId,
                        ["repo"] = analysisRepoName(result) ?? Path.GetFileName(request.RepoPath.TrimEnd('/', '\\')),
                        ["mode"] = request.Incremental ? "update" : "generate",
                        ["dryRun"] = request.DryRun.ToString(),
                        ["success"] = result.Success.ToString(),
                        ["offline"] = result.UsedOfflineFallback.ToString(),
                        ["error"] = result.Error,
                        ["provider"] = request.ProviderOverride ?? request.Config.Provider,
                        ["model"] = request.ModelOverride ?? request.Config.DefaultModel
                    },
                    measurements = new Dictionary<string, double>
                    {
                        ["durationMs"] = result.Duration.TotalMilliseconds,
                        ["inputTokens"] = result.InputTokens,
                        ["outputTokens"] = result.OutputTokens,
                        ["filesWritten"] = result.FilesWritten.Count,
                        ["filesWouldCreate"] = result.FilesWouldCreate.Count,
                        ["filesWouldUpdate"] = result.FilesWouldUpdate.Count,
                        ["moduleCount"] = result.ModuleCount,
                        ["estimatedUsd"] = result.CostEstimate is null
                            ? 0
                            : (double)result.CostEstimate.EstimatedUsd
                    }
                }
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(endpoint, content).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogDebug(
                "Application Insights ingestion returned {Status}",
                (int)response.StatusCode);
        }
    }

    private static string? analysisRepoName(GenerationResult result) => result.Analysis?.RepoName;

    private static void ParseConnectionString(
        string connectionString,
        out string? ingestionEndpoint,
        out string? instrumentationKey)
    {
        ingestionEndpoint = null;
        instrumentationKey = null;
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            var key = part[..idx].Trim();
            var value = part[(idx + 1)..].Trim();
            if (key.Equals("InstrumentationKey", StringComparison.OrdinalIgnoreCase)
                || key.Equals("instrumentationkey", StringComparison.OrdinalIgnoreCase))
            {
                instrumentationKey = value;
            }
            else if (key.Equals("IngestionEndpoint", StringComparison.OrdinalIgnoreCase))
            {
                ingestionEndpoint = value;
            }
        }
    }
}

/// <summary>No-op telemetry used when Application Insights is not configured.</summary>
public sealed class NullRunTelemetry : IRunTelemetry
{
    public bool IsEnabled => false;

    public void TrackRun(WikiGenerationRequest request, GenerationResult result)
    {
        // intentionally empty
    }
}
