using AgentWiki.Desktop.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace AgentWiki.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private async void OnBrowseRepoClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select repository root",
            AllowMultiple = false
        }).ConfigureAwait(true);

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            if (Vm is not null)
            {
                await Vm.SetRepoPathAsync(path).ConfigureAwait(true);
            }
        }
    }

    private async void OnLoadRepoClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || string.IsNullOrWhiteSpace(Vm.RepoPath))
        {
            return;
        }

        await Vm.SetRepoPathAsync(Vm.RepoPath).ConfigureAwait(true);
    }

    private async void OnRecentSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: string path } && Vm is not null)
        {
            await Vm.SetRepoPathAsync(path).ConfigureAwait(true);
        }
    }
}
