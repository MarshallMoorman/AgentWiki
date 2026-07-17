using System.Collections.ObjectModel;
using AgentWiki.Core;
using AgentWiki.Core.Models;
using AgentWiki.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentWiki.Desktop.ViewModels;

public partial class WikiBrowserViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _repoPath = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevealInOsCommand))]
    private string _wikiRoot = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevealInOsCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenExternalCommand))]
    private string _selectedPath = "";

    [ObservableProperty]
    private string _markdownText = "Select a wiki file to preview.";

    [ObservableProperty]
    private string _status = "";

    /// <summary>When true, show raw Markdown source instead of the rendered preview.</summary>
    [ObservableProperty]
    private bool _showRaw;

    public ObservableCollection<WikiTreeNode> Roots { get; } = [];

    public void BindRepo(string repoPath, AgentWikiConfig? config)
    {
        RepoPath = repoPath;
        WikiRoot = config is null
            ? Path.Combine(repoPath, Constants.Paths.DefaultOutputPath)
            : PathResolver.ResolveOutput(config, repoPath);
        Refresh();
        RefreshCommandStates();
    }

    /// <summary>Re-evaluate gated commands after tab load / bind.</summary>
    public void RefreshCommandStates()
    {
        RevealInOsCommand.NotifyCanExecuteChanged();
        OpenExternalCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Refresh()
    {
        Roots.Clear();
        MarkdownText = "Select a wiki file to preview.";
        SelectedPath = "";

        if (string.IsNullOrWhiteSpace(WikiRoot) || !Directory.Exists(WikiRoot))
        {
            Status = $"Wiki folder not found: {WikiRoot}. Run Generate first.";
            return;
        }

        Status = WikiRoot;
        var root = BuildNode(new DirectoryInfo(WikiRoot), isRoot: true);
        if (root is not null)
        {
            Roots.Add(root);
        }
    }

    private static WikiTreeNode? BuildNode(DirectoryInfo dir, bool isRoot)
    {
        var node = new WikiTreeNode
        {
            Name = isRoot ? Constants.Paths.DefaultOutputPath : dir.Name,
            FullPath = dir.FullName,
            IsDirectory = true
        };

        foreach (var childDir in dir.GetDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            var child = BuildNode(childDir, isRoot: false);
            if (child is not null)
            {
                node.Children.Add(child);
            }
        }

        foreach (var file in dir.GetFiles("*.md").OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            node.Children.Add(new WikiTreeNode
            {
                Name = file.Name,
                FullPath = file.FullName,
                IsDirectory = false
            });
        }

        return node.Children.Count > 0 || isRoot ? node : null;
    }

    /// <summary>Directory of the currently previewed document (for relative links).</summary>
    public string CurrentDocumentDirectory =>
        string.IsNullOrWhiteSpace(SelectedPath)
            ? WikiRoot
            : (Path.GetDirectoryName(SelectedPath) ?? WikiRoot);

    [RelayCommand]
    private async Task OpenNodeAsync(WikiTreeNode? node)
    {
        if (node is null || node.IsDirectory)
        {
            return;
        }

        await OpenFileAsync(node.FullPath).ConfigureAwait(true);
    }

    /// <summary>
    /// Opens a wiki markdown file by absolute path (tree selection or in-document link).
    /// </summary>
    public async Task OpenFileAsync(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return;
        }

        try
        {
            var full = Path.GetFullPath(absolutePath);

            // Directory link → try index.md or open in OS.
            if (Directory.Exists(full))
            {
                var index = Path.Combine(full, "index.md");
                if (File.Exists(index))
                {
                    full = index;
                }
                else
                {
                    MainViewModel.OpenInOs(full);
                    Status = $"Opened folder: {full}";
                    return;
                }
            }

            if (!File.Exists(full))
            {
                Status = $"Link target not found: {full}";
                return;
            }

            // Non-markdown → open externally.
            if (!full.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                && !full.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
            {
                MainViewModel.OpenInOs(full);
                Status = $"Opened externally: {full}";
                return;
            }

            SelectedPath = full;
            ShowRaw = false;
            MarkdownText = await File.ReadAllTextAsync(full).ConfigureAwait(true);
            Status = full;

            // Expand/select matching tree node when possible (best-effort).
            SelectTreeNodeByPath(full);
        }
        catch (Exception ex)
        {
            ShowRaw = true;
            MarkdownText = $"Failed to open: {ex.Message}";
            Status = ex.Message;
        }
    }

    private void SelectTreeNodeByPath(string absolutePath)
    {
        // No tree selection API from VM alone; the view listens for SelectedPath changes
        // if it wants to sync. Status/path display is enough for navigation feedback.
        _ = absolutePath;
    }

    [RelayCommand(CanExecute = nameof(CanRevealInOs))]
    private void RevealInOs()
    {
        var path = !string.IsNullOrWhiteSpace(SelectedPath) ? SelectedPath : WikiRoot;
        if (!string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)))
        {
            MainViewModel.OpenInOs(path);
        }
    }

    private bool CanRevealInOs()
    {
        var path = !string.IsNullOrWhiteSpace(SelectedPath) ? SelectedPath : WikiRoot;
        return !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path));
    }

    [RelayCommand(CanExecute = nameof(CanOpenExternal))]
    private void OpenExternal()
    {
        if (!string.IsNullOrWhiteSpace(SelectedPath) && File.Exists(SelectedPath))
        {
            MainViewModel.OpenInOs(SelectedPath);
        }
    }

    private bool CanOpenExternal() =>
        !string.IsNullOrWhiteSpace(SelectedPath) && File.Exists(SelectedPath);
}

public sealed class WikiTreeNode
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public ObservableCollection<WikiTreeNode> Children { get; } = [];
}
