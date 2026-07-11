using System.ClientModel.Primitives;
using System.Reflection;
using AgentWiki.App.Services;
using AgentWiki.Core.Models;

namespace AgentWiki.Cli.Tests.Services;

public sealed class SemanticKernelTimeoutTests
{
    [Fact]
    public void CreateOpenAiClient_SetsNetworkTimeoutFromConfig()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(630) };
        var client = SemanticKernelLlmCompletionService.CreateOpenAiClient(
            apiKey: "sk-test",
            endpoint: null,
            httpClient: http,
            timeoutSeconds: 600);

        // OpenAIClient stores options privately; inspect via ClientPipeline default comparison
        // and ensure construction succeeds with explicit NetworkTimeout.
        client.ShouldNotBeNull();

        // Reflect ClientPipelineOptions.NetworkTimeout if accessible through client options field.
        var options = GetClientOptions(client);
        options.ShouldNotBeNull();
        options!.NetworkTimeout.ShouldBe(TimeSpan.FromSeconds(600));
        options.RetryPolicy.ShouldNotBeNull();
    }

    [Fact]
    public void CreateAzureOpenAiClient_SetsNetworkTimeoutFromConfig()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(630) };
        var config = new AgentWikiConfig
        {
            AzureOpenAI =
            {
                Endpoint = "https://example.openai.azure.com/",
                ApiKey = "azure-key",
                UseManagedIdentity = false
            }
        };

        var client = SemanticKernelLlmCompletionService.CreateAzureOpenAiClient(
            config,
            endpoint: config.AzureOpenAI.Endpoint!,
            httpClient: http,
            timeoutSeconds: 600);

        client.ShouldNotBeNull();
        var options = GetClientOptions(client);
        options.ShouldNotBeNull();
        options!.NetworkTimeout.ShouldBe(TimeSpan.FromSeconds(600));
    }

    [Fact]
    public void ClientPipeline_DefaultNetworkTimeout_Is100Seconds()
    {
        // Documents the SDK footgun that caused ~5 minute failures with retries.
        var field = typeof(ClientPipeline).GetField(
            "<DefaultNetworkTimeout>k__BackingField",
            BindingFlags.NonPublic | BindingFlags.Static);
        field.ShouldNotBeNull();
        var value = (TimeSpan)field!.GetValue(null)!;
        value.ShouldBe(TimeSpan.FromSeconds(100));
    }

    private static ClientPipelineOptions? GetClientOptions(object client)
    {
        // Walk private fields for a ClientPipelineOptions / OpenAIClientOptions instance.
        for (var type = client.GetType(); type is not null && type != typeof(object); type = type.BaseType)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (typeof(ClientPipelineOptions).IsAssignableFrom(field.FieldType))
                {
                    return field.GetValue(client) as ClientPipelineOptions;
                }

                var nested = field.GetValue(client);
                if (nested is null || nested is string || nested.GetType().IsPrimitive)
                {
                    continue;
                }

                // One level deep (e.g. _client → options)
                foreach (var inner in nested.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (typeof(ClientPipelineOptions).IsAssignableFrom(inner.FieldType))
                    {
                        return inner.GetValue(nested) as ClientPipelineOptions;
                    }
                }
            }
        }

        return null;
    }
}
