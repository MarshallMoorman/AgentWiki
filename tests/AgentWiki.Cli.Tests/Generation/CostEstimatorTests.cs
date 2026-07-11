using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;

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
        estimate.FormatUsd().ShouldStartWith("~");
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

    [Fact]
    public void Estimate_ConfigGlobalOverride_Wins()
    {
        var config = new AgentWikiConfig
        {
            InputUsdPerMillionTokens = 1m,
            OutputUsdPerMillionTokens = 2m
        };

        var estimate = CostEstimator.Estimate("gpt-4o", 1_000_000, 1_000_000, config);
        estimate.EstimatedUsd.ShouldBe(3m);
        estimate.InputUsdPerMillion.ShouldBe(1m);
        estimate.OutputUsdPerMillion.ShouldBe(2m);
    }

    [Fact]
    public void Estimate_ConfigModelPricing_MatchesDeploymentName()
    {
        var config = new AgentWikiConfig
        {
            ModelPricing =
            {
                ["my-gpt"] = new ModelPricingEntry
                {
                    InputUsdPerMillion = 10m,
                    OutputUsdPerMillion = 20m
                }
            }
        };

        var estimate = CostEstimator.Estimate("azure-my-gpt-prod", 1_000_000, 0, config);
        estimate.EstimatedUsd.ShouldBe(10m);
    }
}
