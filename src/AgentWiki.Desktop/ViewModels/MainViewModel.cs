using System.Collections.ObjectModel;
using AgentWiki.App.Infrastructure;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Constants;
using AgentWiki.Core.Models;
using AgentWiki.Desktop.Models;
using AgentWiki.Desktop.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentWiki.Desktop.ViewModels;

/// <summary>Shell view-model: repo selection, navigation, status bar, child pages.</summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly IConfigLoader _configLoader;
    private readonly UiSettingsStore _uiSettingsStore;
    private UiSettings _uiSettings = new();

    public MainViewModel(
        IConfigLoader configLoader,
        UiSettingsStore uiSettingsStore,
        DashboardViewModel dashboard,
        GenerateViewModel generate,
        UpdateViewModel update,
        SetupViewModel setup,
        SettingsViewModel settings,
        ProviderViewModel provider,
        WikiBrowserViewModel wiki,
        LogsViewModel logs)
    {
        _configLoader = configLoader;
        _uiSettingsStore = uiSettingsStore;
        Dashboard = dashboard;
        Generate = generate;
        Update = update;
        Setup = setup;
        Settings = settings;
        Provider = provider;
        Wiki = wiki;
        Logs = logs;

        NavItems = new ObservableCollection<NavItem>(NavItem.DefaultItems);
        SelectedNav = NavItems[0];
        CurrentPage = NavPage.Dashboard;
        CurrentContent = Dashboard;

        _ = InitializeAsync();
    }

    public DashboardViewModel Dashboard { get; }
    public GenerateViewModel Generate { get; }
    public UpdateViewModel Update { get; }
    public SetupViewModel Setup { get; }
    public SettingsViewModel Settings { get; }
    public ProviderViewModel Provider { get; }
    public WikiBrowserViewModel Wiki { get; }
    public LogsViewModel Logs { get; }

    public string AppTitle => "AgentWiki Desktop";
    public string Version => AgentWikiConstants.Version;
    public string LogDirectory => AgentWikiLogging.LogDirectory;
    public string TodayLogPath => AgentWikiLogging.TodayLogFilePath;

    public ObservableCollection<string> RecentRepos { get; } = [];
    public ObservableCollection<NavItem> NavItems { get; }

    [ObservableProperty]
    private string _repoPath = "";

    [ObservableProperty]
    private string _repoDisplay = "(no repository)";

    [ObservableProperty]
    private NavPage _currentPage;

    [ObservableProperty]
    private NavItem? _selectedNav;

    [ObservableProperty]
    private ViewModelBase? _currentContent;

    [ObservableProperty]
    private string _statusProvider = "—";

    [ObservableProperty]
    private string _statusModel = "—";

    [ObservableProperty]
    private string _statusTimeout = "—";

    [ObservableProperty]
    private string _statusReady = "—";

    [ObservableProperty]
    private bool _isLlmReady;

    [ObservableProperty]
    private string _statusMessage = "Select a repository to begin.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private AgentWikiConfig? _effectiveConfig;

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is null || value.Page == CurrentPage)
        {
            return;
        }

        CurrentPage = value.Page;
    }

    partial void OnCurrentPageChanged(NavPage value)
    {
        // Keep sidebar selection in sync when navigated programmatically.
        if (SelectedNav?.Page != value)
        {
            SelectedNav = NavItems.FirstOrDefault(n => n.Page == value);
        }

        CurrentContent = value switch
        {
            NavPage.Dashboard => Dashboard,
            NavPage.Generate => Generate,
            NavPage.Update => Update,
            NavPage.Setup => Setup,
            NavPage.Settings => Settings,
            NavPage.Provider => Provider,
            NavPage.Wiki => Wiki,
            NavPage.Logs => Logs,
            _ => Dashboard
        };

        _ = RefreshCurrentPageAsync();
    }

    private async Task InitializeAsync()
    {
        _uiSettings = await _uiSettingsStore.LoadAsync().ConfigureAwait(true);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ThemeService.Apply(_uiSettings.Theme);
            RecentRepos.Clear();
            foreach (var r in _uiSettings.RecentRepos)
            {
                RecentRepos.Add(r);
            }
        });

        if (!string.IsNullOrWhiteSpace(_uiSettings.LastRepoPath)
            && Directory.Exists(_uiSettings.LastRepoPath))
        {
            await SetRepoPathAsync(_uiSettings.LastRepoPath).ConfigureAwait(true);
        }
        else
        {
            // Default to current working directory if it looks like a repo.
            var cwd = Directory.GetCurrentDirectory();
            if (Directory.Exists(Path.Combine(cwd, ".git"))
                || File.Exists(Path.Combine(cwd, "AgentWiki.slnx")))
            {
                await SetRepoPathAsync(cwd).ConfigureAwait(true);
            }
        }
    }

    /// <summary>Applies and persists a theme preference (system / dark / light).</summary>
    public async Task SetThemeAsync(string preference)
    {
        var normalized = ThemeService.Normalize(preference);
        _uiSettings.Theme = normalized;
        await Dispatcher.UIThread.InvokeAsync(() => ThemeService.Apply(normalized));
        await _uiSettingsStore.SaveAsync(_uiSettings).ConfigureAwait(true);
        StatusMessage = $"Theme set to {ThemeService.DisplayName(normalized)}.";
    }

    /// <summary>Current theme preference from UI settings.</summary>
    public string CurrentTheme => ThemeService.Normalize(_uiSettings.Theme);

    public async Task SetRepoPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            IsBusy = true;
            var resolved = PathResolver.ResolveRepo(path);
            if (!Directory.Exists(resolved))
            {
                StatusMessage = $"Path does not exist: {resolved}";
                return;
            }

            RepoPath = resolved;
            RepoDisplay = PathResolver.DisplayHomeRelative(resolved);
            _uiSettings.RememberRepo(resolved);
            await _uiSettingsStore.SaveAsync(_uiSettings).ConfigureAwait(true);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RecentRepos.Clear();
                foreach (var r in _uiSettings.RecentRepos)
                {
                    RecentRepos.Add(r);
                }
            });

            await ReloadConfigAsync().ConfigureAwait(true);
            await RefreshCurrentPageAsync().ConfigureAwait(true);
            StatusMessage = $"Repository: {RepoDisplay}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to open repo: {ex.Message}";
            AgentWikiLogging.LogError(ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ReloadConfigAsync()
    {
        if (string.IsNullOrWhiteSpace(RepoPath))
        {
            EffectiveConfig = null;
            StatusProvider = StatusModel = StatusTimeout = StatusReady = "—";
            return;
        }

        var config = await _configLoader.LoadAsync(RepoPath).ConfigureAwait(true);
        config = _configLoader.ApplyCliOverrides(config, repoPath: RepoPath);
        EffectiveConfig = config;

        var provider = LlmSettings.NormalizeProvider(config.Provider);
        var model = LlmSettings.ResolveModel(config);
        var notReady = LlmSettings.DescribeNotReadyReason(config);

        StatusProvider = provider;
        StatusModel = model;
        StatusTimeout = $"{config.LlmTimeoutSeconds}s";
        IsLlmReady = notReady is null;
        StatusReady = notReady is null ? "LLM ready" : $"Offline · {notReady}";
    }

    private async Task RefreshCurrentPageAsync()
    {
        if (string.IsNullOrWhiteSpace(RepoPath))
        {
            return;
        }

        try
        {
            switch (CurrentPage)
            {
                case NavPage.Dashboard:
                    await Dashboard.LoadAsync(RepoPath, EffectiveConfig).ConfigureAwait(true);
                    break;
                case NavPage.Generate:
                    Generate.BindRepo(RepoPath, EffectiveConfig);
                    break;
                case NavPage.Update:
                    Update.BindRepo(RepoPath, EffectiveConfig);
                    break;
                case NavPage.Setup:
                    Setup.BindRepo(RepoPath);
                    break;
                case NavPage.Settings:
                    await Settings.LoadAsync(RepoPath).ConfigureAwait(true);
                    break;
                case NavPage.Provider:
                    Provider.BindRepo(RepoPath, EffectiveConfig);
                    break;
                case NavPage.Wiki:
                    Wiki.BindRepo(RepoPath, EffectiveConfig);
                    break;
                case NavPage.Logs:
                    await Logs.LoadAsync().ConfigureAwait(true);
                    break;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            AgentWikiLogging.LogError(ex.Message, ex);
        }
    }

    [RelayCommand]
    private void Navigate(NavPage page) => CurrentPage = page;

    [RelayCommand]
    private async Task SelectRecentAsync(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            await SetRepoPathAsync(path).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ClearRecentAsync()
    {
        _uiSettings.ClearRecent();
        await _uiSettingsStore.SaveAsync(_uiSettings).ConfigureAwait(true);
        RecentRepos.Clear();
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(AgentWikiLogging.LogDirectory);
            OpenInOs(AgentWikiLogging.LogDirectory);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenWikiFolder()
    {
        if (EffectiveConfig is null || string.IsNullOrWhiteSpace(RepoPath))
        {
            return;
        }

        try
        {
            var output = PathResolver.ResolveOutput(EffectiveConfig, RepoPath);
            Directory.CreateDirectory(output);
            OpenInOs(output);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    internal static void OpenInOs(string path)
    {
        if (OperatingSystem.IsMacOS())
        {
            System.Diagnostics.Process.Start("open", path);
        }
        else if (OperatingSystem.IsWindows())
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        else
        {
            System.Diagnostics.Process.Start("xdg-open", path);
        }
    }
}
