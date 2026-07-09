using AgentWiki.Core.Models;

namespace AgentWiki.Core.Generation;

/// <summary>
/// Rough token cost estimator for reporting (not billing-grade).
/// Prices are approximate USD per 1M tokens and can be overridden via config later.
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
            ["o4-mini"] = (1.10m, 4.40m),
            ["default"] = (2.50m, 10.00m)
        };

    /// <summary>
    /// Estimates USD cost for the given token usage and model name.
    /// </summary>
    public static CostEstimate Estimate(string? model, int inputTokens, int outputTokens)
    {
        var key = string.IsNullOrWhiteSpace(model) ? "default" : model.Trim();
        if (!ModelRates.TryGetValue(key, out var rates))
        {
            // Fallback: try prefix match (e.g. deployment names containing gpt-4o)
            rates = ModelRates.FirstOrDefault(kv =>
                key.Contains(kv.Key, StringComparison.OrdinalIgnoreCase)).Value;
            if (rates == default)
            {
                rates = ModelRates["default"];
                key = "default";
            }
        }

        var inputCost = inputTokens / 1_000_000m * rates.InputPerMillion;
        var outputCost = outputTokens / 1_000_000m * rates.OutputPerMillion;

        return new CostEstimate
        {
            Model = key,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            EstimatedUsd = decimal.Round(inputCost + outputCost, 6),
            Note = "Approximate estimate only; actual billing depends on provider/deployment pricing."
        };
    }

    public static CostEstimate Estimate(string? model, TokenUsage? usage) =>
        Estimate(model, usage?.InputTokens ?? 0, usage?.OutputTokens ?? 0);
}

/// <summary>Rough cost report for a generation run.</summary>
public sealed class CostEstimate
{
    public required string Model { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public decimal EstimatedUsd { get; init; }
    public string Note { get; init; } = "";
}
