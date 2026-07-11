using System.ClientModel;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace AgentWiki.App.Services;

/// <summary>
/// Builds resilience pipelines for LLM calls (retries for true transient HTTP/transport failures only).
/// </summary>
public static class LlmResilience
{
    /// <summary>Max retry attempts after the first try (total attempts = MaxRetryAttempts + 1).</summary>
    public const int MaxRetryAttempts = 3;

    public static ResiliencePipeline CreatePipeline(ILogger logger) =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = MaxRetryAttempts,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                // Do NOT retry pure timeouts — they already burned the HttpClient budget.
                // DO retry connection resets / IO / nested SK HttpOperationException wrappers.
                ShouldHandle = new PredicateBuilder()
                    .Handle<Exception>(IsRetryableFailure),
                OnRetry = args =>
                {
                    logger.LogWarning(
                        "LLM call failed (attempt {Attempt}/{Max}); retrying in {Delay}: {Message}",
                        args.AttemptNumber + 1,
                        MaxRetryAttempts,
                        args.RetryDelay,
                        args.Outcome.Exception?.Message ?? "unknown error");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();

    /// <summary>
    /// True when the failure is worth retrying (transient HTTP, connection reset, nested SK wrappers).
    /// Pure timeouts and user cancellations are not retried.
    /// </summary>
    public static bool IsRetryableFailure(Exception? ex)
    {
        if (ex is null)
        {
            return false;
        }

        // Pure timeout (no transport reset) — do not retry.
        if (IsTimeout(ex) && !HasTransportReset(ex))
        {
            return false;
        }

        for (var current = ex; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case HttpRequestException http when !IsTimeout(http):
                    return true;
                case IOException:
                case SocketException:
                    return true;
                case ClientResultException clientEx when IsTransientStatusCode((int)clientEx.Status):
                    return true;
            }

            if (ContainsNetworkFailureMessage(current.Message))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTransientStatusCode(int status) =>
        // 0 = no HTTP response (connection dropped before status)
        status is 0 or 408 or 429 || status >= 500;

    private static bool HasTransportReset(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is SocketException
                || current is IOException
                || ContainsNetworkFailureMessage(current.Message))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsNetworkFailureMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        return message.Contains("Connection reset", StringComparison.OrdinalIgnoreCase)
               || message.Contains("connection was closed", StringComparison.OrdinalIgnoreCase)
               || message.Contains("forcibly closed", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Unable to read data from the transport", StringComparison.OrdinalIgnoreCase)
               || message.Contains("An error occurred while sending the request", StringComparison.OrdinalIgnoreCase)
               || message.Contains("name or service not known", StringComparison.OrdinalIgnoreCase)
               || message.Contains("No such host", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Connection refused", StringComparison.OrdinalIgnoreCase)
               || message.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase);
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
