using System.Collections;
using AgentWiki.Desktop.Services;
using AgentWiki.Desktop.ViewModels;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ColorTextBlock.Avalonia;
using Markdown.Avalonia;

namespace AgentWiki.Desktop.Views;

public partial class WikiBrowserView : UserControl
{
    private bool _styleFixed;
    private WikiHyperlinkCommand? _linkCommand;

    public WikiBrowserView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => EnsureMarkdownConfigured();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is WikiBrowserViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            EnsureMarkdownConfigured();
            UpdateAssetPathRoot(vm);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (DataContext is not WikiBrowserViewModel vm)
        {
            return;
        }

        if (e.PropertyName is nameof(WikiBrowserViewModel.MarkdownText)
            or nameof(WikiBrowserViewModel.ShowRaw))
        {
            Dispatcher.UIThread.Post(SanitizeTree, DispatcherPriority.Send);
            Dispatcher.UIThread.Post(SanitizeTree, DispatcherPriority.Loaded);
        }

        if (e.PropertyName is nameof(WikiBrowserViewModel.SelectedPath)
            or nameof(WikiBrowserViewModel.WikiRoot))
        {
            UpdateAssetPathRoot(vm);
        }
    }

    private void EnsureMarkdownConfigured()
    {
        if (this.FindControl<MarkdownScrollViewer>("MarkdownPreview") is not { } viewer)
        {
            return;
        }

        if (!_styleFixed)
        {
            try
            {
                viewer.MarkdownStyleName = "";
            }
            catch
            {
                // optional
            }

            MarkdownFontFix.Apply(viewer);
            // Allow pointer interaction with hyperlinks.
            viewer.SelectionEnabled = false;
            _styleFixed = true;
        }

        WireHyperlinks(viewer);
        if (DataContext is WikiBrowserViewModel vm)
        {
            UpdateAssetPathRoot(vm);
        }
    }

    private void WireHyperlinks(MarkdownScrollViewer viewer)
    {
        // Always rebind so the command closes over the current DataContext.
        _linkCommand = new WikiHyperlinkCommand(
            getBaseDirectory: () =>
                DataContext is WikiBrowserViewModel vm
                    ? vm.CurrentDocumentDirectory
                    : "",
            openLocalWikiPathAsync: async path =>
            {
                if (DataContext is WikiBrowserViewModel vm)
                {
                    await vm.OpenFileAsync(path).ConfigureAwait(true);
                }
            });

        // MdAvPlugins.HyperlinkCommand is what Markdown.Avalonia uses for CHyperlink clicks.
        viewer.Plugins.HyperlinkCommand = _linkCommand;
    }

    private void UpdateAssetPathRoot(WikiBrowserViewModel vm)
    {
        if (this.FindControl<MarkdownScrollViewer>("MarkdownPreview") is not { } viewer)
        {
            return;
        }

        var root = vm.CurrentDocumentDirectory;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            root = vm.WikiRoot;
        }

        if (!string.IsNullOrWhiteSpace(root))
        {
            viewer.AssetPathRoot = root;
            if (viewer.Plugins.PathResolver is not null)
            {
                viewer.Plugins.PathResolver.AssetPathRoot = root;
            }
        }
    }

    private void SanitizeTree()
    {
        if (this.FindControl<MarkdownScrollViewer>("MarkdownPreview") is { } viewer)
        {
            // Re-apply command in case Plugins was rebuilt with content.
            WireHyperlinks(viewer);
            MarkdownFontFix.SanitizeVisualTree(viewer);
            // Some Markdown.Avalonia builds snapshot the command at parse time;
            // rebind CHyperlink.Command so clicks always hit our handler.
            RebindHyperlinkActions(viewer);
        }
    }

    private void RebindHyperlinkActions(MarkdownScrollViewer viewer)
    {
        if (_linkCommand is null)
        {
            return;
        }

        foreach (var ctext in viewer.GetVisualDescendants().OfType<CTextBlock>())
        {
            RebindInlines(ctext.Content);
        }
    }

    private void RebindInlines(IEnumerable? inlines)
    {
        if (inlines is null || _linkCommand is null)
        {
            return;
        }

        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case CHyperlink link:
                    // Capture command for this click; parameter is the href.
                    link.Command = href => _linkCommand.Execute(href);
                    break;
                case CSpan span:
                    RebindInlines(span.Content);
                    break;
            }
        }
    }

    private async void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not TreeView tree
            || tree.SelectedItem is not WikiTreeNode node
            || DataContext is not WikiBrowserViewModel vm)
        {
            return;
        }

        try
        {
            EnsureMarkdownConfigured();
            await vm.OpenNodeCommand.ExecuteAsync(node);
            Dispatcher.UIThread.Post(SanitizeTree, DispatcherPriority.Send);
            Dispatcher.UIThread.Post(SanitizeTree, DispatcherPriority.Loaded);
            Dispatcher.UIThread.Post(SanitizeTree, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            await FallbackToRawAsync(vm, node, ex).ConfigureAwait(true);
        }
    }

    private static async Task FallbackToRawAsync(WikiBrowserViewModel vm, WikiTreeNode node, Exception ex)
    {
        vm.ShowRaw = true;
        vm.Status = $"Preview failed: {ex.Message}";
        if (!node.IsDirectory && File.Exists(node.FullPath))
        {
            try
            {
                vm.MarkdownText = await File.ReadAllTextAsync(node.FullPath).ConfigureAwait(true);
                vm.SelectedPath = node.FullPath;
                return;
            }
            catch
            {
                // fall through
            }
        }

        vm.MarkdownText = $"Preview failed ({ex.GetType().Name}): {ex.Message}";
    }
}
