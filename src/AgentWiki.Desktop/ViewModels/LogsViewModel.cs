using System.Collections.ObjectModel;
using AgentWiki.App.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentWiki.Desktop.ViewModels;

public partial class LogsViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    private string _logDirectory = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshTailCommand))]
    private string? _selectedFile;

    [ObservableProperty]
    private string _tailText = "";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private bool _autoRefresh;

    private CancellationTokenSource? _autoCts;

    public ObservableCollection<string> LogFiles { get; } = [];

    public async Task LoadAsync()
    {
        LogDirectory = AgentWikiLogging.LogDirectory;
        if (string.IsNullOrWhiteSpace(LogDirectory))
        {
            AgentWikiLogging.Configure(verbose: false, enableConsoleSink: false);
            LogDirectory = AgentWikiLogging.LogDirectory;
        }

        Directory.CreateDirectory(LogDirectory);
        LogFiles.Clear();
        foreach (var file in Directory.GetFiles(LogDirectory, "agent-wiki-*.log")
                     .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase))
        {
            LogFiles.Add(file);
        }

        SelectedFile = LogFiles.FirstOrDefault(f =>
            string.Equals(f, AgentWikiLogging.TodayLogFilePath, StringComparison.OrdinalIgnoreCase))
            ?? LogFiles.FirstOrDefault();

        await RefreshTailAsync().ConfigureAwait(true);
        Status = $"Log directory: {LogDirectory}";
        RefreshCommandStates();
    }

    /// <summary>Re-evaluate gated commands after tab load.</summary>
    public void RefreshCommandStates()
    {
        RefreshTailCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
        OpenSelectedCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedFileChanged(string? value) => _ = RefreshTailAsync();

    partial void OnAutoRefreshChanged(bool value)
    {
        _autoCts?.Cancel();
        _autoCts?.Dispose();
        _autoCts = null;
        if (value)
        {
            _autoCts = new CancellationTokenSource();
            _ = AutoRefreshLoopAsync(_autoCts.Token);
        }
    }

    private async Task AutoRefreshLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(2000, token).ConfigureAwait(true);
                await RefreshTailAsync().ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // expected
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefreshTail))]
    private async Task RefreshTailAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedFile) || !File.Exists(SelectedFile))
        {
            TailText = "(no log file selected)";
            return;
        }

        try
        {
            // Shared file may be locked by Serilog; open with FileShare.ReadWrite.
            await using var stream = new FileStream(
                SelectedFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync().ConfigureAwait(true);
            var lines = content.Split('\n');
            var tail = lines.Length <= 200 ? lines : lines.TakeLast(200).ToArray();
            TailText = string.Join('\n', tail);
            Status = $"{SelectedFile} · showing last {tail.Length} lines";
        }
        catch (Exception ex)
        {
            TailText = $"Could not read log: {ex.Message}";
        }
    }

    private bool CanRefreshTail() =>
        !string.IsNullOrWhiteSpace(SelectedFile) && File.Exists(SelectedFile);

    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private void OpenFolder()
    {
        Directory.CreateDirectory(LogDirectory);
        MainViewModel.OpenInOs(LogDirectory);
    }

    private bool CanOpenFolder() => !string.IsNullOrWhiteSpace(LogDirectory);

    [RelayCommand(CanExecute = nameof(CanOpenSelected))]
    private void OpenSelected()
    {
        if (!string.IsNullOrWhiteSpace(SelectedFile) && File.Exists(SelectedFile))
        {
            MainViewModel.OpenInOs(SelectedFile);
        }
    }

    private bool CanOpenSelected() =>
        !string.IsNullOrWhiteSpace(SelectedFile) && File.Exists(SelectedFile);

    [RelayCommand]
    private async Task CopyPathAsync()
    {
        var path = SelectedFile ?? LogDirectory;
        // Clipboard requires a visual root; store path in status for manual copy if unavailable.
        Status = $"Path: {path}";
        await Task.CompletedTask;
    }
}
