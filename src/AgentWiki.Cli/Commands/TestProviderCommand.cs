using System.ComponentModel;
using System.Diagnostics;
using AgentWiki.Core.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentWiki.Cli.Commands;

/// <summary>
/// Verifies that the configured LLM provider accepts a minimal chat completion.
/// </summary>
public sealed class TestProviderCommand(
    IConfigLoader configLoader,
    ILlmCompletionService llm) : AsyncCommand<TestProviderCommand.Settings>
{
    /// <summary>CLI settings for <c>agent-wiki test-provider</c>.</summary>
    public sealed class Settings : CommandSettingsBase
    {
        [CommandOption("-m|--model <MODEL>")]
        [Description("Model or Azure deployment override")]
        public string? Model { get; init; }

        [CommandOption("--provider <PROVIDER>")]
        [Description("Provider override: azure-openai | openai | github-models")]
        public string? Provider { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        AnsiConsole.MarkupLine("[bold blue]AgentWiki[/] — test LLM provider");

        // Ensure .env is loaded for this repo path before reading config.
        var config = await configLoader
            .LoadAsync(settings.RepoPath, settings.ConfigPath)
            .ConfigureAwait(false);

        config = configLoader.ApplyCliOverrides(
            config,
            repoPath: settings.RepoPath,
            model: settings.Model,
            provider: settings.Provider);

        var provider = settings.Provider ?? config.Provider;
        var model = settings.Model
            ?? (IsOpenAiFamily(provider)
                ? config.OpenAI.Model ?? config.DefaultModel
                : config.AzureOpenAI.DeploymentName ?? config.DefaultModel);

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold]Resolved connection[/]")
            .AddColumn("Key")
            .AddColumn("Value");

        table.AddRow("Repo", Markup.Escape(config.RepoPath));
        table.AddRow("Provider", Markup.Escape(provider));
        table.AddRow("Model", Markup.Escape(model ?? "—"));
        table.AddRow("Can use live LLM", llm.CanUseLiveLlm(config, provider) ? "[green]yes[/]" : "[red]no[/]");

        if (IsOpenAiFamily(provider))
        {
            table.AddRow("OpenAI endpoint", string.IsNullOrWhiteSpace(config.OpenAI.Endpoint)
                ? "[grey](default api.openai.com)[/]"
                : Markup.Escape(config.OpenAI.Endpoint));
            table.AddRow("OpenAI API key", string.IsNullOrWhiteSpace(config.OpenAI.ApiKey) ? "[red](not set)[/]" : "[green]***[/]");
        }
        else
        {
            table.AddRow("Azure endpoint", string.IsNullOrWhiteSpace(config.AzureOpenAI.Endpoint)
                ? "[red](not set)[/]"
                : Markup.Escape(Redact(config.AzureOpenAI.Endpoint)));
            table.AddRow("Deployment", Markup.Escape(config.AzureOpenAI.DeploymentName ?? model ?? "—"));
            table.AddRow("Azure API key", string.IsNullOrWhiteSpace(config.AzureOpenAI.ApiKey) ? "[grey](not set)[/]" : "[green]***[/]");
            table.AddRow("Managed identity", config.AzureOpenAI.UseManagedIdentity ? "yes" : "no");
        }

        AnsiConsole.Write(table);

        if (!llm.CanUseLiveLlm(config, provider))
        {
            AnsiConsole.MarkupLine("[red]✗[/] Provider is not configured for a live call.");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("Configure one of:");
            AnsiConsole.MarkupLine("  • [cyan].agentwiki/config.json[/] ([cyan]openAI.apiKey[/] or [cyan]azureOpenAI.*[/])");
            AnsiConsole.MarkupLine("  • [cyan].env[/] in the repo root ([cyan]AGENTWIKI_OpenAI__ApiKey[/], etc.)");
            AnsiConsole.MarkupLine("  • process environment variables with the [cyan]AGENTWIKI_[/] prefix");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("Tip: [cyan]config.json[/] is best for non-secrets; put API keys in [cyan].env[/] or CI secrets.");
            return 2;
        }

        LlmCompletionResult? result = null;
        Exception? error = null;
        var sw = Stopwatch.StartNew();

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Sending probe chat completion…", async _ =>
            {
                try
                {
                    result = await llm.CompleteAsync(
                            config,
                            systemPrompt: "You are a connectivity probe. Reply with a short confirmation only.",
                            userPrompt: "Reply with exactly: AgentWiki provider OK",
                            modelOverride: settings.Model,
                            providerOverride: settings.Provider,
                            options: LlmRequestOptions.ConnectivityProbe,
                            cancellationToken: default)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            })
            .ConfigureAwait(false);

        sw.Stop();

        if (error is not null)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Provider call failed after {sw.Elapsed.TotalSeconds:F2}s");
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error.Message)}[/]");
            if (error.InnerException is not null)
            {
                AnsiConsole.MarkupLine($"[grey]{Markup.Escape(error.InnerException.Message)}[/]");
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("Common fixes:");
            AnsiConsole.MarkupLine("  • Wrong API key or expired key");
            AnsiConsole.MarkupLine("  • Model / deployment name not available on this account");
            AnsiConsole.MarkupLine("  • Provider mismatch (e.g. provider=openai but only Azure keys set)");
            AnsiConsole.MarkupLine("  • Network / firewall blocking the endpoint");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Provider responded in {sw.Elapsed.TotalSeconds:F2}s");

        var resultTable = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Key")
            .AddColumn("Value");

        resultTable.AddRow("Provider", Markup.Escape(result!.Provider));
        resultTable.AddRow("Model", Markup.Escape(result.Model));
        resultTable.AddRow("Input tokens", (result.TokenUsage?.InputTokens ?? 0).ToString());
        resultTable.AddRow("Output tokens", (result.TokenUsage?.OutputTokens ?? 0).ToString());
        resultTable.AddRow("Reply", Markup.Escape(Truncate(result.Content, 200)));
        AnsiConsole.Write(resultTable);

        AnsiConsole.MarkupLine("[grey]Configuration looks good for generate/update LLM steps.[/]");
        return 0;
    }

    private static bool IsOpenAiFamily(string? provider)
    {
        var p = (provider ?? "").Trim().ToLowerInvariant();
        return p is "openai" or "oai" or "github-models" or "github" or "githubmodels";
    }

    private static string Redact(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            ? $"{uri.Scheme}://{uri.Host}/"
            : endpoint;

    private static string Truncate(string value, int max)
    {
        var trimmed = value.Replace('\n', ' ').Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..(max - 1)] + "…";
    }
}
