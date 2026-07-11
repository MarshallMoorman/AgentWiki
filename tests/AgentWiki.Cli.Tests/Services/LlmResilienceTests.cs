using System.Net.Sockets;
using AgentWiki.App.Services;

namespace AgentWiki.Cli.Tests.Services;

public sealed class LlmResilienceTests
{
    [Fact]
    public void IsRetryableFailure_HttpRequestException_IsRetryable()
    {
        LlmResilience.IsRetryableFailure(new HttpRequestException("boom")).ShouldBeTrue();
    }

    [Fact]
    public void IsRetryableFailure_ConnectionResetNestedLikeSemanticKernel_IsRetryable()
    {
        // Mirrors the production stack from the 1.2.1 LoanView generate log:
        // outer wrapper → HttpRequestException → IOException → SocketException(ConnectionReset)
        var socket = new SocketException((int)SocketError.ConnectionReset);
        var io = new IOException(
            "Unable to read data from the transport connection: Connection reset by peer.",
            socket);
        var http = new HttpRequestException("An error occurred while sending the request.", io);
        var skWrap = new Exception("An error occurred while sending the request.", http);

        LlmResilience.IsRetryableFailure(skWrap).ShouldBeTrue();
    }

    [Fact]
    public void IsRetryableFailure_Timeout_IsNotRetryable()
    {
        LlmResilience.IsRetryableFailure(new TimeoutException("HttpClient.Timeout of 100s elapsed"))
            .ShouldBeFalse();
        LlmResilience.IsRetryableFailure(new TaskCanceledException("The operation was canceled."))
            .ShouldBeFalse();
    }

    [Fact]
    public void IsRetryableFailure_ArgumentException_IsNotRetryable()
    {
        LlmResilience.IsRetryableFailure(new ArgumentException("bad config")).ShouldBeFalse();
    }

    [Fact]
    public void IsRetryableFailure_SocketExceptionAlone_IsRetryable()
    {
        LlmResilience.IsRetryableFailure(new SocketException((int)SocketError.ConnectionReset))
            .ShouldBeTrue();
    }
}
