using System.Collections.ObjectModel;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Models;
using AgentWiki.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentWiki.Desktop.ViewModels;

public partial class SettingsViewModel(
    IConfigLoader configLoader,
    ConfigEditorService configEditor,
    UiSettingsStore uiSettingsStore) : ViewModelBase
{
    private string _repoPath = "";
    private string? _existingOpenAiKey;
    private string? _existingAzureKey;
    private bool _suppressThemeSave;

    [ObservableProperty]
    private string _outputPath = "docs/wiki";

    [ObservableProperty]
    private string _defaultModel = "gpt-4o";

    [ObservableProperty]
    private string _provider = "azure-openai";

    [ObservableProperty]
    private string _agentMdPath = "AGENTS.md";

    [ObservableProperty]
    private int _maxFilesToAnalyze = 500;

    [ObservableProperty]
    private bool _enableIncrementalUpdates = true;

    [ObservableProperty]
    private int _llmTimeoutSeconds = 300;

    [ObservableProperty]
    private int _maxLlmSummaryChars = 16_000;

    [ObservableProperty]
    private string _ignorePatternsText = "";

    [ObservableProperty]
    private string? _azureEndpoint;

    [ObservableProperty]
    private string? _azureDeployment;

    [ObservableProperty]
    private bool _azureUseManagedIdentity;

    [ObservableProperty]
    private string? _openAiEndpoint;

    [ObservableProperty]
    private string? _openAiModel;

    /// <summary>User-entered OpenAI key (empty means leave unchanged).</summary>
    [ObservableProperty]
    private string _openAiApiKeyInput = "";

    /// <summary>User-entered Azure key (empty means leave unchanged).</summary>
    [ObservableProperty]
    private string _azureApiKeyInput = "";

    [ObservableProperty]
    private string _openAiKeyMask = "(not set)";

    [ObservableProperty]
    private string _azureKeyMask = "(not set)";

    [ObservableProperty]
    private string _message = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isSaving;

    [ObservableProperty]
    private string _themePreference = ThemeService.System;

    public ObservableCollection<KeyValueItem> EffectiveRows { get; } = [];

    public string[] ProviderChoices { get; } =
        ["azure-openai", "openai", "github-models", "offline"];

    public string[] ThemeChoices { get; } = ThemeService.Choices;

    public async Task LoadAsync(string repoPath)
    {
        _repoPath = repoPath;
        Message = "";
        await LoadThemeAsync().ConfigureAwait(true);
        var config = await configLoader.LoadAsync(repoPath).ConfigureAwait(true);
        config = configLoader.ApplyCliOverrides(config, repoPath: repoPath);

        OutputPath = config.OutputPath;
        DefaultModel = config.DefaultModel;
        Provider = config.Provider;
        AgentMdPath = config.AgentMdPath;
        MaxFilesToAnalyze = config.MaxFilesToAnalyze;
        EnableIncrementalUpdates = config.EnableIncrementalUpdates;
        LlmTimeoutSeconds = config.LlmTimeoutSeconds;
        MaxLlmSummaryChars = config.MaxLlmSummaryChars;
        IgnorePatternsText = string.Join(Environment.NewLine, config.IgnorePatterns);
        AzureEndpoint = config.AzureOpenAI.Endpoint;
        AzureDeployment = config.AzureOpenAI.DeploymentName;
        AzureUseManagedIdentity = config.AzureOpenAI.UseManagedIdentity;
        OpenAiEndpoint = config.OpenAI.Endpoint;
        OpenAiModel = config.OpenAI.Model;

        _existingOpenAiKey = config.OpenAI.ApiKey;
        _existingAzureKey = config.AzureOpenAI.ApiKey;
        OpenAiKeyMask = string.IsNullOrWhiteSpace(_existingOpenAiKey)
            ? "(not set)"
            : ConfigEditorService.MaskSecret(_existingOpenAiKey);
        AzureKeyMask = string.IsNullOrWhiteSpace(_existingAzureKey)
            ? "(not set)"
            : ConfigEditorService.MaskSecret(_existingAzureKey);
        OpenAiApiKeyInput = "";
        AzureApiKeyInput = "";

        RefreshEffective(config);
        RefreshCommandStates();
    }

    /// <summary>Re-evaluate gated commands after tab load.</summary>
    public void RefreshCommandStates()
    {
        SaveCommand.NotifyCanExecuteChanged();
        ReloadCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadThemeAsync()
    {
        _suppressThemeSave = true;
        try
        {
            var ui = await uiSettingsStore.LoadAsync().ConfigureAwait(true);
            ThemePreference = ThemeService.Normalize(ui.Theme);
            ThemeService.Apply(ThemePreference);
        }
        finally
        {
            _suppressThemeSave = false;
        }
    }

    partial void OnThemePreferenceChanged(string value)
    {
        if (_suppressThemeSave)
        {
            return;
        }

        _ = PersistThemeAsync(value);
    }

    private async Task PersistThemeAsync(string value)
    {
        try
        {
            var normalized = ThemeService.Normalize(value);
            var ui = await uiSettingsStore.LoadAsync().ConfigureAwait(true);
            ui.Theme = normalized;
            ThemeService.Apply(normalized);
            await uiSettingsStore.SaveAsync(ui).ConfigureAwait(true);
            Message = $"Appearance: {ThemeService.DisplayName(normalized)} theme (saved to ~/.agentwiki/ui-settings.json).";
        }
        catch (Exception ex)
        {
            Message = $"Theme save failed: {ex.Message}";
        }
    }

    private void RefreshEffective(AgentWikiConfig config)
    {
        EffectiveRows.Clear();
        EffectiveRows.Add(new("Provider", LlmSettings.NormalizeProvider(config.Provider)));
        EffectiveRows.Add(new("Effective model", LlmSettings.ResolveModel(config)));
        EffectiveRows.Add(new("Timeout", $"{config.LlmTimeoutSeconds}s"));
        EffectiveRows.Add(new("Max summary chars", config.MaxLlmSummaryChars.ToString("N0")));
        EffectiveRows.Add(new("LLM ready", LlmSettings.DescribeNotReadyReason(config) ?? "yes"));
        EffectiveRows.Add(new("Desktop theme", ThemeService.DisplayName(ThemePreference)));
        EffectiveRows.Add(new(
            "Priority",
            "UI/CLI → .env → config.json → AGENTWIKI_* → appsettings"));
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_repoPath) || IsSaving)
        {
            return;
        }

        try
        {
            IsSaving = true;
            var config = new AgentWikiConfig
            {
                RepoPath = ".",
                OutputPath = OutputPath,
                DefaultModel = DefaultModel,
                Provider = Provider,
                AgentMdPath = AgentMdPath,
                MaxFilesToAnalyze = MaxFilesToAnalyze,
                EnableIncrementalUpdates = EnableIncrementalUpdates,
                LlmTimeoutSeconds = LlmTimeoutSeconds,
                MaxLlmSummaryChars = MaxLlmSummaryChars,
                IgnorePatterns = IgnorePatternsText
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList(),
                AzureOpenAI = new AzureOpenAiOptions
                {
                    Endpoint = AzureEndpoint,
                    DeploymentName = AzureDeployment,
                    UseManagedIdentity = AzureUseManagedIdentity,
                    ApiKey = ""
                },
                OpenAI = new OpenAiOptions
                {
                    Endpoint = OpenAiEndpoint,
                    Model = OpenAiModel,
                    ApiKey = ""
                }
            };

            await configEditor.SaveConfigJsonAsync(_repoPath, config).ConfigureAwait(true);

            string? openAi = string.IsNullOrWhiteSpace(OpenAiApiKeyInput) ? null : OpenAiApiKeyInput.Trim();
            string? azure = string.IsNullOrWhiteSpace(AzureApiKeyInput) ? null : AzureApiKeyInput.Trim();
            if (openAi is not null || azure is not null)
            {
                await configEditor.SaveEnvSecretsAsync(_repoPath, openAi, azure).ConfigureAwait(true);
            }

            Message = "Saved .agentwiki/config.json"
                + (openAi is not null || azure is not null ? " and updated .env secrets." : ".");

            await LoadAsync(_repoPath).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Message = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
            RefreshCommandStates();
        }
    }

    private bool CanSave() => !IsSaving && !string.IsNullOrWhiteSpace(_repoPath);

    [RelayCommand(CanExecute = nameof(CanReload))]
    private async Task ReloadAsync()
    {
        if (!string.IsNullOrWhiteSpace(_repoPath))
        {
            await LoadAsync(_repoPath).ConfigureAwait(true);
            Message = "Reloaded effective configuration.";
        }
    }

    private bool CanReload() => !string.IsNullOrWhiteSpace(_repoPath);
}
