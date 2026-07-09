using System.ClientModel;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace AgentWiki.Cli.Services;

/// <summary>
/// Builds resilience pipelines for LLM calls (retries with exponential backoff).
/// </summary>
public static class LlmResilience
{
    public static ResiliencePipeline CreatePipeline(ILogger logger) =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutException>()
                    .Handle<IOException>()
                    .Handle<ClientResultException>(ex => IsTransientStatus(ex)),
                OnRetry = args =>
                {
                    logger.LogWarning(
                        args.Outcome.Exception,
                        "LLM call failed (attempt {Attempt}); retrying in {Delay}",
                        args.AttemptNumber + 1,
                        args.RetryDelay);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();

    private static bool IsTransientStatus(ClientResultException ex)
    {
        // 408/429/5xx are typically retryable.
        var status = (int)ex.Status;
        return status is 408 or 429 || status >= 500;
    }
}
