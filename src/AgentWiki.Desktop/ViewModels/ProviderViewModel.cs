using System.Collections.ObjectModel;
using System.Diagnostics;
using AgentWiki.App.Infrastructure;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Models;
using AgentWiki.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentWiki.Desktop.ViewModels;

public partial class ProviderViewModel(
    IConfigLoader configLoader,
    ILlmCompletionService llm) : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    private string _repoPath = "";

    [ObservableProperty]
    private string? _provider;

    [ObservableProperty]
    private string? _model;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    private bool _isTesting;

    [ObservableProperty]
    private string _message = "Test the configured LLM provider with a minimal chat call.";

    [ObservableProperty]
    private bool? _succeeded;

    public ObservableCollection<KeyValueItem> ConnectionRows { get; } = [];
    public ObservableCollection<KeyValueItem> ResultRows { get; } = [];

    public string[] ProviderChoices { get; } =
        ["azure-openai", "openai", "github-models"];

    public void BindRepo(string repoPath, AgentWikiConfig? config)
    {
        RepoPath = repoPath;
        ConnectionRows.Clear();
        ResultRows.Clear();
        Succeeded = null;

        if (config is not null)
        {
            Provider = config.Provider;
            Model = config.DefaultModel;
            RefreshConnection(config);
        }

        RefreshCommandStates();
    }

    /// <summary>Re-evaluate gated commands after tab load / bind.</summary>
    public void RefreshCommandStates() => TestCommand.NotifyCanExecuteChanged();

    private void RefreshConnection(AgentWikiConfig config)
    {
        ConnectionRows.Clear();
        var provider = string.IsNullOrWhiteSpace(Provider) ? config.Provider : Provider!;
        var canLive = llm.CanUseLiveLlm(config, provider);
        ConnectionRows.Add(new("Repo", config.RepoPath));
        ConnectionRows.Add(new("Provider", provider));
        ConnectionRows.Add(new("Model", Model ?? "—"));
        ConnectionRows.Add(new("Can use live LLM", canLive ? "yes" : "no"));
        ConnectionRows.Add(new(
            "OpenAI API key",
            string.IsNullOrWhiteSpace(config.OpenAI.ApiKey) ? "(not set)" : "***"));
        ConnectionRows.Add(new(
            "Azure API key",
            string.IsNullOrWhiteSpace(config.AzureOpenAI.ApiKey) ? "(not set)" : "***"));
        ConnectionRows.Add(new(
            "Azure endpoint",
            string.IsNullOrWhiteSpace(config.AzureOpenAI.Endpoint)
                ? "(not set)"
                : Redact(config.AzureOpenAI.Endpoint)));
    }

    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task TestAsync()
    {
        if (string.IsNullOrWhiteSpace(RepoPath) || IsTesting)
        {
            return;
        }

        try
        {
            IsTesting = true;
            ResultRows.Clear();
            Succeeded = null;
            Message = "Sending probe chat completion…";

            var config = await configLoader.LoadAsync(RepoPath).ConfigureAwait(true);
            config = configLoader.ApplyCliOverrides(
                config,
                repoPath: RepoPath,
                model: Model,
                provider: Provider);
            RefreshConnection(config);

            if (!llm.CanUseLiveLlm(config, Provider))
            {
                Succeeded = false;
                Message = "Provider is not configured for a live call. Set keys in Settings (.env) or config.";
                return;
            }

            var sw = Stopwatch.StartNew();
            var result = await llm.CompleteAsync(
                    config,
                    systemPrompt: "You are a connectivity probe. Reply with a short confirmation only.",
                    userPrompt: "Reply with exactly: AgentWiki provider OK",
                    modelOverride: Model,
                    providerOverride: Provider,
                    options: LlmRequestOptions.ConnectivityProbe)
                .ConfigureAwait(true);
            sw.Stop();

            Succeeded = true;
            Message = $"Provider responded in {sw.Elapsed.TotalSeconds:F2}s";
            ResultRows.Add(new("Provider", result.Provider));
            ResultRows.Add(new("Model", result.Model));
            ResultRows.Add(new("Input tokens", (result.TokenUsage?.InputTokens ?? 0).ToString()));
            ResultRows.Add(new("Output tokens", (result.TokenUsage?.OutputTokens ?? 0).ToString()));
            ResultRows.Add(new("Reply", Truncate(result.Content, 200)));
            ResultRows.Add(new("Latency", $"{sw.Elapsed.TotalSeconds:F2}s"));
        }
        catch (Exception ex)
        {
            Succeeded = false;
            Message = $"Provider call failed: {ex.Message}";
            AgentWikiLogging.LogError(ex.Message, ex);
            ResultRows.Add(new("Error", ex.Message));
            ResultRows.Add(new("Log", AgentWikiLogging.TodayLogFilePath));
        }
        finally
        {
            IsTesting = false;
            RefreshCommandStates();
        }
    }

    private bool CanTest() => !IsTesting && !string.IsNullOrWhiteSpace(RepoPath);

    [RelayCommand]
    private void OpenLog() => MainViewModel.OpenInOs(AgentWikiLogging.TodayLogFilePath);

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
