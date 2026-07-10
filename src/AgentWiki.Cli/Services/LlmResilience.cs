using System.ClientModel;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace AgentWiki.Cli.Services;

/// <summary>
/// Builds resilience pipelines for LLM calls (retries for true transient HTTP failures only).
/// </summary>
public static class LlmResilience
{
    public static ResiliencePipeline CreatePipeline(ILogger logger) =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                // Do NOT retry timeouts / TaskCanceledException — they already burned the full timeout budget
                // (default HttpClient 100s × 3 retries ≈ several minutes of spinner noise).
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>(ex => !IsTimeout(ex))
                    .Handle<ClientResultException>(IsTransientStatus),
                OnRetry = args =>
                {
                    logger.LogWarning(
                        "LLM call failed (attempt {Attempt}/{Max}); retrying in {Delay}: {Message}",
                        args.AttemptNumber + 1,
                        2,
                        args.RetryDelay,
                        args.Outcome.Exception?.Message ?? "unknown error");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();

    private static bool IsTransientStatus(ClientResultException ex)
    {
        var status = (int)ex.Status;
        return status is 408 or 429 || status >= 500;
    }

    private static bool IsTimeout(Exception ex) =>
        ex is TimeoutException
        || ex is TaskCanceledException
        || ex.InnerException is TimeoutException
        || ex.InnerException is TaskCanceledException
        || (ex.Message?.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ?? false)
        || (ex.Message?.Contains("HttpClient.Timeout", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>
    /// True when the exception is an HTTP/client timeout rather than a user-requested cancel.
    /// </summary>
    public static bool IsTimeoutFailure(Exception ex, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return IsTimeout(ex)
               || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested);
    }
}
