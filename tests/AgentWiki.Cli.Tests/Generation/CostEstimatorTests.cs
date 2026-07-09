using AgentWiki.Core.Generation;

namespace AgentWiki.Cli.Tests.Generation;

public sealed class CostEstimatorTests
{
    [Fact]
    public void Estimate_ComputesPositiveCostForKnownModel()
    {
        var estimate = CostEstimator.Estimate("gpt-4o", inputTokens: 1_000_000, outputTokens: 500_000);

        estimate.EstimatedUsd.ShouldBeGreaterThan(0);
        estimate.Model.ShouldBe("gpt-4o");
        estimate.InputTokens.ShouldBe(1_000_000);
        estimate.OutputTokens.ShouldBe(500_000);
    }

    [Fact]
    public void Estimate_ZeroTokens_IsZeroCost()
    {
        var estimate = CostEstimator.Estimate("gpt-4o", 0, 0);
        estimate.EstimatedUsd.ShouldBe(0);
    }

    [Fact]
    public void Estimate_UnknownModel_UsesFallbackRate()
    {
        var estimate = CostEstimator.Estimate("totally-unknown-model", 1000, 1000);
        estimate.EstimatedUsd.ShouldBeGreaterThanOrEqualTo(0);
    }
}
