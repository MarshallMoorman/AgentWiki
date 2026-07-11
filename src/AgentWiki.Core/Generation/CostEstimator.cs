using AgentWiki.Core.Models;

namespace AgentWiki.Core.Generation;

/// <summary>
/// Rough token cost estimator for reporting (not billing-grade).
/// Uses built-in model rates, optional config overrides, then a default rate.
/// </summary>
public static class CostEstimator
{
    // Approximate public list prices (USD / 1M tokens) — update as needed.
    private static readonly Dictionary<string, (decimal InputPerMillion, decimal OutputPerMillion)> ModelRates =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-4o"] = (2.50m, 10.00m),
            ["gpt-4o-mini"] = (0.15m, 0.60m),
            ["gpt-4.1"] = (2.00m, 8.00m),
            ["gpt-4.1-mini"] = (0.40m, 1.60m),
            ["gpt-4.1-nano"] = (0.10m, 0.40m),
            ["gpt-5"] = (1.25m, 10.00m),
            ["gpt-5-mini"] = (0.25m, 2.00m),
            ["o4-mini"] = (1.10m, 4.40m),
            ["o3-mini"] = (1.10m, 4.40m),
            ["default"] = (2.50m, 10.00m)
        };

    /// <summary>
    /// Estimates USD cost for the given token usage and model name.
    /// </summary>
    public static CostEstimate Estimate(string? model, int inputTokens, int outputTokens) =>
        Estimate(model, inputTokens, outputTokens, config: null);

    /// <summary>
    /// Estimates USD cost using optional config pricing overrides.
    /// </summary>
    public static CostEstimate Estimate(
        string? model,
        int inputTokens,
        int outputTokens,
        AgentWikiConfig? config)
    {
        var key = string.IsNullOrWhiteSpace(model) ? "default" : model.Trim();
        var rates = ResolveRates(key, config);

        var inputCost = inputTokens / 1_000_000m * rates.InputPerMillion;
        var outputCost = outputTokens / 1_000_000m * rates.OutputPerMillion;

        return new CostEstimate
        {
            Model = rates.ModelKey,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            EstimatedUsd = decimal.Round(inputCost + outputCost, 6),
            InputUsdPerMillion = rates.InputPerMillion,
            OutputUsdPerMillion = rates.OutputPerMillion,
            Note = "Approximate estimate only; actual billing depends on provider/deployment pricing."
        };
    }

    public static CostEstimate Estimate(string? model, TokenUsage? usage) =>
        Estimate(model, usage?.InputTokens ?? 0, usage?.OutputTokens ?? 0);

    public static CostEstimate Estimate(string? model, TokenUsage? usage, AgentWikiConfig? config) =>
        Estimate(model, usage?.InputTokens ?? 0, usage?.OutputTokens ?? 0, config);

    private static (string ModelKey, decimal InputPerMillion, decimal OutputPerMillion) ResolveRates(
        string key,
        AgentWikiConfig? config)
    {
        // Explicit global overrides win when both set.
        if (config is { InputUsdPerMillionTokens: { } inRate, OutputUsdPerMillionTokens: { } outRate }
            && inRate >= 0 && outRate >= 0)
        {
            return (key, inRate, outRate);
        }

        // Per-model table from config
        if (config?.ModelPricing is { Count: > 0 })
        {
            foreach (var entry in config.ModelPricing)
            {
                if (key.Equals(entry.Key, StringComparison.OrdinalIgnoreCase)
                    || key.Contains(entry.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return (entry.Key, entry.Value.InputUsdPerMillion, entry.Value.OutputUsdPerMillion);
                }
            }
        }

        if (ModelRates.TryGetValue(key, out var rates))
        {
            return (key, rates.InputPerMillion, rates.OutputPerMillion);
        }

        // Prefix / contains match for deployment names
        var match = ModelRates.FirstOrDefault(kv =>
            !kv.Key.Equals("default", StringComparison.OrdinalIgnoreCase)
            && key.Contains(kv.Key, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(match.Key))
        {
            return (match.Key, match.Value.InputPerMillion, match.Value.OutputPerMillion);
        }

        var fallback = ModelRates["default"];
        return ("default", fallback.InputPerMillion, fallback.OutputPerMillion);
    }
}

/// <summary>Rough cost report for a generation run.</summary>
public sealed class CostEstimate
{
    public required string Model { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public decimal EstimatedUsd { get; init; }
    public decimal InputUsdPerMillion { get; init; }
    public decimal OutputUsdPerMillion { get; init; }
    public string Note { get; init; } = "";

    public string FormatUsd() => EstimatedUsd < 0.0001m && (InputTokens + OutputTokens) > 0
        ? $"~{EstimatedUsd:F6}"
        : $"~{EstimatedUsd:F4}";
}
